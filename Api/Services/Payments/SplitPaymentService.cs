using Api.Data;
using Api.Dtos.Splits.Requests;
using Api.Models.Splits;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Api.Services.Payments;

public sealed class SplitPaymentService : ISplitPaymentService
{
    private readonly LightningDbContext _db;
    public SplitPaymentService(LightningDbContext db) => _db = db;

    public async Task SetAsync(Guid splitId, Guid ownerId, Guid participantId, SetPaymentDto dto)
    {
        var split = await _db.SplitSessions
            .FirstOrDefaultAsync(s => s.Id == splitId && s.OwnerId == ownerId && s.IsFinalized);
        if (split is null) throw new KeyNotFoundException("Split not found or not finalized.");

        var participantExists = await _db.SplitParticipants
            .AnyAsync(p => p.Id == participantId && p.SplitSessionId == splitId);
        if (!participantExists) throw new KeyNotFoundException("Participant not in split.");

        var row = await _db.SplitPayments
            .FirstOrDefaultAsync(x => x.SplitSessionId == splitId && x.ParticipantId == participantId);

        if (row is null)
        {
            row = new SplitPayment
            {
                Id = Guid.NewGuid(),
                SplitSessionId = splitId,
                ParticipantId = participantId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.SplitPayments.Add(row);
        }

        row.IsPaid = dto.IsPaid;
        row.PaidAt = dto.IsPaid ? DateTimeOffset.UtcNow : null;
        row.PlatformKey = dto.PlatformKey;
        row.Amount = dto.Amount;
        row.Note = dto.Note;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();
    }
}
