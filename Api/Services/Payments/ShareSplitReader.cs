using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Data;
using Api.Dtos.Splits.Responses;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Api.Services.Payments;

public sealed class SplitShareReader : ISplitShareReader
{
    private readonly LightningDbContext _db;
    private readonly IPaymentLinkBuilder _links;

    public SplitShareReader(LightningDbContext db, IPaymentLinkBuilder links)
    {
        _db = db;
        _links = links;
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

        // Paid state per participant (owner-managed)
        var paidMap = await _db.SplitPayments
            .AsNoTracking()
            .Where(x => x.SplitSessionId == split.Id)
            .ToDictionaryAsync(x => x.ParticipantId, x => x.IsPaid);

        var people = new List<ShareParticipantDto>(result.Participants.Count);
        foreach (var p in result.Participants.OrderBy(x => x.DisplayName))
        {
            var links = await _links.BuildManyAsync(ownerMethods, p.Total, $"Lightning Split {split.Id.ToString()[..8]}");
            var isPaid = paidMap.TryGetValue(p.ParticipantId, out var paid) && paid;
            people.Add(new ShareParticipantDto(p.ParticipantId, p.DisplayName, p.Total, links, isPaid));
        }

        return new ShareSplitResponseDto(split.Id, code, people);
    }
}
