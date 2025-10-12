using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Api.Dtos.Splits.Responses;
using Api.Models.Splits;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Api.Services.Payments;

public sealed class SplitShareReader : ISplitShareReader
{
    private readonly LightningDbContext _db;
    private readonly IPaymentLinkBuilder _links;
    private readonly IParticipantTokenService _tokenService;

    public SplitShareReader(LightningDbContext db, IPaymentLinkBuilder links, IParticipantTokenService tokenService)
    {
        _db = db;
        _links = links;
        _tokenService = tokenService;
    }

    public async Task<ShareSplitResponseDto> GetByCodeAsync(string code)
    {
        var split = await _db.SplitSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ShareCode == code && s.IsFinalized);

        if (split is null) throw new KeyNotFoundException("Share code not found.");

        var result = await _db.SplitResults
            .AsNoTracking()
            .Include(r => r.Participants)
            .Where(r => r.SplitSessionId == split.Id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstAsync();

        var ownerMethods = await _db.OwnerPayoutMethods
            .AsNoTracking()
            .Include(m => m.Platform)
            .Where(m => m.OwnerId == split.OwnerId)
            .OrderByDescending(m => m.IsDefault)
            .ThenBy(m => m.Platform.SortOrder)
            .ToListAsync();

        var people = new List<ShareParticipantDto>(result.Participants.Count);
        foreach (var p in result.Participants.OrderBy(x => x.DisplayName))
        {
            var links = await _links.BuildManyAsync(ownerMethods, p.Total, $"Lightning Split {split.Id.ToString()[..8]}");
            var isPaid = p.PaymentStatus == PaymentStatus.Paid;
            
            // Generate participant token for self-reporting payment
            var participantToken = _tokenService.GenerateToken(split.Id, p.ParticipantId);
            
            people.Add(new ShareParticipantDto(
                p.ParticipantId,
                p.DisplayName,
                p.Total,
                links,
                isPaid,
                p.PaymentStatus,
                p.PaymentUpdatedAt,
                p.PaymentSource,
                p.PaymentNote,
                participantToken));
        }

        return new ShareSplitResponseDto(split.Id, code, people);
    }
}
