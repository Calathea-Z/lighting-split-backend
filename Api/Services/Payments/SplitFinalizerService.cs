using Api.Data;
using Api.Dtos.Splits.Responses;
using Api.Models.Splits;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;


namespace Api.Services.Payments;

public sealed class SplitFinalizerService : ISplitFinalizerService
{
    private readonly LightningDbContext _db;
    private readonly ISplitCalculator _calc;
    private readonly IPaymentLinkBuilder _links;

    public SplitFinalizerService(LightningDbContext db, ISplitCalculator calc, IPaymentLinkBuilder links)
    {
        _db = db;
        _calc = calc;
        _links = links;
    }

    public async Task<FinalizeSplitResponse> FinalizeAsync(Guid splitId, Guid ownerId, string baseUrl)
    {
        var split = await _db.SplitSessions
            .Include(s => s.Participants)
            .FirstOrDefaultAsync(s => s.Id == splitId && s.OwnerId == ownerId);

        if (split is null) throw new KeyNotFoundException("Split not found.");

        // Idempotent: if already finalized, return existing payload
        if (split.IsFinalized && !string.IsNullOrWhiteSpace(split.ShareCode))
            return await BuildResponseAsync(split, baseUrl);

        // Compute current preview
        var preview = await _calc.PreviewAsync(splitId);

        // Persist snapshot
        var snapshot = new SplitResult
        {
            Id = Guid.NewGuid(),
            SplitSessionId = split.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            Participants = [.. preview.Participants.Select(p => new SplitParticipantResult
            {
                Id = Guid.NewGuid(),
                ParticipantId = p.ParticipantId,
                DisplayName = p.DisplayName,
                ItemsSubtotal = p.ItemsSubtotal,
                DiscountAlloc = p.DiscountAlloc,
                TaxAlloc = p.TaxAlloc,
                TipAlloc = p.TipAlloc,
                Total = p.Total
            })]
        };
        _db.SplitResults.Add(snapshot);

        // Share code + flags
        split.ShareCode ??= await GenerateUniqueCodeAsync();
        split.IsFinalized = true;
        split.FinalizedAt = DateTimeOffset.UtcNow;
        split.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();

        return await BuildResponseAsync(split, baseUrl);
    }

    private async Task<FinalizeSplitResponse> BuildResponseAsync(SplitSession split, string baseUrl)
    {
        var result = await _db.SplitResults
            .Include(r => r.Participants)
            .Where(r => r.SplitSessionId == split.Id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstAsync();

        var ownerMethods = await _db.OwnerPayoutMethods
            .Include(m => m.Platform)
            .Where(m => m.OwnerId == split.OwnerId)
            .OrderByDescending(m => m.IsDefault)
            .ThenBy(m => m.Platform.SortOrder)
            .ToListAsync();

        var participants = new List<FinalizeParticipantDto>();
        foreach (var p in result.Participants.OrderBy(x => x.DisplayName))
        {
            var links = await _links.BuildManyAsync(ownerMethods, p.Total, $"Lightning Split {split.Id.ToString()[..8]}");
            participants.Add(new FinalizeParticipantDto(p.ParticipantId, p.DisplayName, p.Total, links));
        }

        return new FinalizeSplitResponse(
            split.Id,
            split.ShareCode!,
            JoinBase(baseUrl, split.ShareCode!),
            participants
        );
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        using var rng = RandomNumberGenerator.Create();

        var buf = new byte[8];

        while (true)
        {
            rng.GetBytes(buf);

            var codeChars = new char[8];
            for (int i = 0; i < 8; i++)
                codeChars[i] = alphabet[buf[i] % alphabet.Length];

            var code = new string(codeChars);

            var exists = await _db.SplitSessions.AnyAsync(s => s.ShareCode == code);
            if (!exists) return code;
        }
    }

    private static string JoinBase(string baseUrl, string code)
    => $"{baseUrl.TrimEnd('/')}/s/{code}";

}
