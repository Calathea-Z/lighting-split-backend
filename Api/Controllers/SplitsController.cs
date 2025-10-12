using Api.Data;
using Api.Dtos.Splits.Common;
using Api.Dtos.Splits.Requests;
using Api.Dtos.Splits.Responses;
using Api.Infrastructure.Middleware;
using Api.Models.Events;
using Api.Models.Splits;
using Api.Services.Events.Abstractions;
using Api.Services.Payments;
using Api.Services.Payments.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Route("api/splits")]
public sealed class SplitsController : ControllerBase
{
    private readonly LightningDbContext _db;
    private readonly ISplitCalculator _calc;
    private readonly ISplitFinalizerService _splitterFinalizerService;
    private readonly ISplitPaymentService _splitPaymentService;
    private readonly IEventStreamService _eventStream;

    public SplitsController(
        LightningDbContext db,
        ISplitCalculator calc,
        ISplitFinalizerService splitterFinalizerService,
        ISplitPaymentService splitPaymentService,
        IEventStreamService eventStream)
    {
        _db = db;
        _calc = calc;
        _splitterFinalizerService = splitterFinalizerService;
        _splitPaymentService = splitPaymentService;
        _eventStream = eventStream;
    }

    // GET /api/splits
    [HttpGet]
    public async Task<ActionResult<ListSplitResponseDto>> List()
    {
        var owner = HttpContext.GetOwner();

        var splits = await _db.SplitSessions
            .Where(s => s.OwnerId == owner.Id)
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new ListSplitResponseDto
            {
                Id = s.Id,
                Name = s.Name ?? string.Empty,
                ReceiptId = s.ReceiptId,
                IsFinalized = s.IsFinalized,
                HasShareCode = !string.IsNullOrEmpty(s.ShareCode),
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync();

        return Ok(splits);
    }

    // GET /api/splits/{id}/preview
    [HttpGet("{id:guid}/preview")]
    public async Task<ActionResult<SplitPreviewDto>> Preview([FromRoute] Guid id)
    {
        var owner = HttpContext.GetOwner();

        // ensure the split belongs to owner
        var own = await _db.SplitSessions.AnyAsync(s => s.Id == id && s.OwnerId == owner.Id);
        if (!own) return NotFound();

        var dto = await _calc.PreviewAsync(id);
        return Ok(dto);
    }

    // POST /api/splits
    [HttpPost]
    public async Task<ActionResult<CreateSplitResponseDto>> Create([FromBody] CreateSplitDto req)
    {
        var owner = HttpContext.GetOwner();

        var exists = await _db.Receipts.AnyAsync(r => r.Id == req.ReceiptId);
        if (!exists) return NotFound("Receipt not found.");

        var s = new SplitSession
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            ReceiptId = req.ReceiptId,
            Name = req.Name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.Add(s);
        await _db.SaveChangesAsync();
        return Ok(new CreateSplitResponseDto(s.Id));
    }

    // POST /api/splits/{id}/participants
    [HttpPost("{id:guid}/participants")]
    public async Task<IActionResult> AddParticipant([FromRoute] Guid id, [FromBody] CreateSplitParticipantDto req)
    {
        var owner = HttpContext.GetOwner();

        var s = await _db.SplitSessions.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == owner.Id);
        if (s is null) return NotFound();

        var p = new SplitParticipant
        {
            Id = Guid.NewGuid(),
            SplitSessionId = s.Id,
            DisplayName = req.DisplayName,
            SortOrder = req.SortOrder ?? 0
        };
        _db.Add(p);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // POST /api/splits/{id}/claims
    [HttpPost("{id:guid}/claims")]
    public async Task<IActionResult> UpsertClaims([FromRoute] Guid id, [FromQuery] bool replace, [FromBody] UpsertItemClaimsDto req)
    {
        var owner = HttpContext.GetOwner();

        var s = await _db.SplitSessions.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == owner.Id);
        if (s is null) return NotFound();

        // Normalize incoming list to a mutable list of ItemClaimDto
        var incoming = (req?.Claims ?? Array.Empty<ItemClaimDto>()).ToList();

        // Allow clearing all when replace=true and no claims provided
        if (replace && incoming.Count == 0)
        {
            var toClear = await _db.ItemClaims.Where(x => x.SplitSessionId == s.Id).ToListAsync();
            if (toClear.Count > 0)
            {
                _db.ItemClaims.RemoveRange(toClear);
                await _db.SaveChangesAsync();
            }
            return NoContent();
        }

        // Validate participants belong to split & items belong to receipt
        var partIds = await _db.SplitParticipants
            .Where(p => p.SplitSessionId == s.Id)
            .Select(p => p.Id)
            .ToHashSetAsync();

        var itemIds = await _db.ReceiptItems
            .Where(i => i.ReceiptId == s.ReceiptId)
            .Select(i => i.Id)
            .ToHashSetAsync();

        foreach (var c in incoming)
        {
            if (!partIds.Contains(c.ParticipantId)) return BadRequest("Participant not in split.");
            if (!itemIds.Contains(c.ReceiptItemId)) return BadRequest("Item not in receipt.");
            if (c.QtyShare < 0m) return BadRequest("QtyShare must be >= 0.");
        }

        if (replace)
        {
            var existingAll = await _db.ItemClaims.Where(x => x.SplitSessionId == s.Id).ToListAsync();
            if (existingAll.Count > 0) _db.ItemClaims.RemoveRange(existingAll);
        }

        // EF-friendly lookup of existing (item, participant) pairs
        var claimItemIds = incoming.Select(c => c.ReceiptItemId).Distinct().ToList();
        var claimPartIds = incoming.Select(c => c.ParticipantId).Distinct().ToList();

        var existing = await _db.ItemClaims
            .Where(x => x.SplitSessionId == s.Id
                && claimItemIds.Contains(x.ReceiptItemId)
                && claimPartIds.Contains(x.ParticipantId))
            .ToListAsync();

        var existingMap = existing.ToDictionary(x => (x.ReceiptItemId, x.ParticipantId));

        // Upsert rows
        foreach (var c in incoming)
        {
            if (existingMap.TryGetValue((c.ReceiptItemId, c.ParticipantId), out var row))
            {
                row.QtyShare = c.QtyShare;
            }
            else
            {
                _db.ItemClaims.Add(new ItemClaim
                {
                    Id = Guid.NewGuid(),
                    SplitSessionId = s.Id,
                    ReceiptItemId = c.ReceiptItemId,
                    ParticipantId = c.ParticipantId,
                    QtyShare = c.QtyShare
                });
            }
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }


    // POST /api/splits/{id}/finalize
    [HttpPost("{id:guid}/finalize")]
    public async Task<ActionResult<FinalizeSplitResponse>> Finalize([FromRoute] Guid id)
    {
        var owner = HttpContext.GetOwner();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var dto = await _splitterFinalizerService.FinalizeAsync(id, owner.Id, baseUrl);
        return Ok(dto);
    }

    // POST /api/splits/{id}/share/rotate
    [HttpPost("{id:guid}/share/rotate")]
    public async Task<ActionResult<object>> RotateShare([FromRoute] Guid id, [FromServices] IShareCodeService codes)
    {
        var owner = HttpContext.GetOwner();

        var s = await _db.SplitSessions.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == owner.Id);
        if (s is null) return NotFound();

        // Ensure split is finalized before allowing share code rotation
        if (!s.IsFinalized)
        {
            return BadRequest("Split must be finalized before generating share code.");
        }

        s.ShareCode = await codes.GenerateUniqueAsync();
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Ok(new { shareCode = s.ShareCode, shareUrl = $"{baseUrl}/s/{s.ShareCode}" });
    }

    // POST /api/splits/{id}/share/revoke
    [HttpPost("{id:guid}/share/revoke")]
    public async Task<IActionResult> RevokeShare([FromRoute] Guid id)
    {
        var owner = HttpContext.GetOwner();

        var s = await _db.SplitSessions.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == owner.Id);
        if (s is null) return NotFound();

        s.ShareCode = null;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // PATCH /api/splits/{id}/participants/{participantId}/payment
    [HttpPatch("{id:guid}/participants/{participantId:guid}/payment")]
    public async Task<IActionResult> SetPayment(
        [FromRoute] Guid id,
        [FromRoute] Guid participantId,
        [FromBody] SetPaymentDto dto)
    {
        var owner = HttpContext.GetOwner();

        try
        {
            await _splitPaymentService.SetAsync(id, owner.Id, participantId, dto);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // PATCH /api/splits/{id}/participants/{participantId}/payment-status
    [HttpPatch("{id:guid}/participants/{participantId:guid}/payment-status")]
    public async Task<IActionResult> UpdatePaymentStatus(
        [FromRoute] Guid id,
        [FromRoute] Guid participantId,
        [FromBody] UpdatePaymentStatusDto dto)
    {
        var owner = HttpContext.GetOwner();

        // Verify split ownership
        var split = await _db.SplitSessions
            .FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == owner.Id);
        if (split is null) return NotFound("Split not found");

        // Get the latest split result
        var result = await _db.SplitResults
            .Include(r => r.Participants)
            .Where(r => r.SplitSessionId == id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();
        if (result is null) return NotFound("Split not finalized");

        // Find the participant result
        var participantResult = result.Participants
            .FirstOrDefault(p => p.ParticipantId == participantId);
        if (participantResult is null) return NotFound("Participant not found");

        // Store old status for audit
        var oldStatus = participantResult.PaymentStatus;

        // Update payment status
        participantResult.PaymentStatus = dto.Status;
        participantResult.PaymentUpdatedAt = DateTimeOffset.UtcNow;
        participantResult.PaymentSource = dto.Source;
        participantResult.PaymentNote = dto.Note;

        // Create audit event
        var auditEvent = new PaymentStatusEvent
        {
            Id = Guid.NewGuid(),
            SplitResultId = result.Id,
            SplitParticipantResultId = participantResult.Id,
            OldStatus = oldStatus,
            NewStatus = dto.Status,
            Source = dto.Source ?? "manual",
            ActorId = owner.Id,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = dto.CorrelationId,
            Notes = dto.Note
        };

        _db.PaymentStatusEvents.Add(auditEvent);
        await _db.SaveChangesAsync();

        // Emit real-time event
        var statusEvent = new PaymentStatusChangedEvent
        {
            SplitResultId = result.Id,
            ParticipantResultId = participantResult.Id,
            Status = dto.Status.ToString(),
            Source = dto.Source ?? "manual",
            Timestamp = DateTimeOffset.UtcNow,
            ParticipantName = participantResult.DisplayName,
            Amount = participantResult.Total
        };
        await _eventStream.EmitPaymentStatusChangedAsync(statusEvent);

        return NoContent();
    }

    // POST /api/splits/{id}/participants/{participantId}/self-report
    [HttpPost("{id:guid}/participants/{participantId:guid}/self-report")]
    public async Task<IActionResult> SelfReportPayment(
        [FromRoute] Guid id,
        [FromRoute] Guid participantId,
        [FromBody] SelfReportPaymentDto dto,
        [FromServices] IParticipantTokenService tokenService)
    {
        // Validate token
        var tokenData = tokenService.ValidateToken(dto.Token);
        if (tokenData == null || tokenData.Value.splitId != id || tokenData.Value.participantId != participantId)
        {
            return BadRequest("Invalid or expired token");
        }

        // Get the latest split result
        var result = await _db.SplitResults
            .Include(r => r.Participants)
            .Where(r => r.SplitSessionId == id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();
        if (result is null) return NotFound("Split not finalized");

        // Find the participant result
        var participantResult = result.Participants
            .FirstOrDefault(p => p.ParticipantId == participantId);
        if (participantResult is null) return NotFound("Participant not found");

        // Store old status for audit
        var oldStatus = participantResult.PaymentStatus;

        // Update payment status to SelfReported
        participantResult.PaymentStatus = PaymentStatus.SelfReported;
        participantResult.PaymentUpdatedAt = DateTimeOffset.UtcNow;
        participantResult.PaymentSource = "self-reported";
        participantResult.PaymentNote = dto.Note;

        // Create audit event
        var auditEvent = new PaymentStatusEvent
        {
            Id = Guid.NewGuid(),
            SplitResultId = result.Id,
            SplitParticipantResultId = participantResult.Id,
            OldStatus = oldStatus,
            NewStatus = PaymentStatus.SelfReported,
            Source = "self-reported",
            ActorId = null, // No authenticated user
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid().ToString(),
            Notes = dto.Note
        };

        _db.PaymentStatusEvents.Add(auditEvent);
        await _db.SaveChangesAsync();

        // Emit real-time event
        var statusEvent = new PaymentStatusChangedEvent
        {
            SplitResultId = result.Id,
            ParticipantResultId = participantResult.Id,
            Status = PaymentStatus.SelfReported.ToString(),
            Source = "self-reported",
            Timestamp = DateTimeOffset.UtcNow,
            ParticipantName = participantResult.DisplayName,
            Amount = participantResult.Total
        };
        await _eventStream.EmitPaymentStatusChangedAsync(statusEvent);

        return NoContent();
    }

    // POST /api/splits/{id}/participants/{participantId}/verify
    [HttpPost("{id:guid}/participants/{participantId:guid}/verify")]
    public async Task<IActionResult> VerifyPayment(
        [FromRoute] Guid id,
        [FromRoute] Guid participantId,
        [FromBody] VerifyPaymentDto dto)
    {
        var owner = HttpContext.GetOwner();

        // Verify split ownership
        var split = await _db.SplitSessions
            .FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == owner.Id);
        if (split is null) return NotFound("Split not found");

        // Get the latest split result
        var result = await _db.SplitResults
            .Include(r => r.Participants)
            .Where(r => r.SplitSessionId == id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();
        if (result is null) return NotFound("Split not finalized");

        // Find the participant result
        var participantResult = result.Participants
            .FirstOrDefault(p => p.ParticipantId == participantId);
        if (participantResult is null) return NotFound("Participant not found");

        // Store old status for audit
        var oldStatus = participantResult.PaymentStatus;

        // Update payment status to Verified
        participantResult.PaymentStatus = PaymentStatus.Verified;
        participantResult.PaymentUpdatedAt = DateTimeOffset.UtcNow;
        participantResult.PaymentSource = "owner-verified";
        participantResult.PaymentNote = dto.Note;
        participantResult.VerifiedByUserId = owner.Id;

        // Create audit event
        var auditEvent = new PaymentStatusEvent
        {
            Id = Guid.NewGuid(),
            SplitResultId = result.Id,
            SplitParticipantResultId = participantResult.Id,
            OldStatus = oldStatus,
            NewStatus = PaymentStatus.Verified,
            Source = "owner-verified",
            ActorId = owner.Id,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid().ToString(),
            Notes = dto.Note
        };

        _db.PaymentStatusEvents.Add(auditEvent);
        await _db.SaveChangesAsync();

        // Emit real-time event
        var statusEvent = new PaymentStatusChangedEvent
        {
            SplitResultId = result.Id,
            ParticipantResultId = participantResult.Id,
            Status = PaymentStatus.Verified.ToString(),
            Source = "owner-verified",
            Timestamp = DateTimeOffset.UtcNow,
            ParticipantName = participantResult.DisplayName,
            Amount = participantResult.Total
        };
        await _eventStream.EmitPaymentStatusChangedAsync(statusEvent);

        return NoContent();
    }

    // POST /api/splits/{id}/participants/{participantId}/revert
    [HttpPost("{id:guid}/participants/{participantId:guid}/revert")]
    public async Task<IActionResult> RevertPayment(
        [FromRoute] Guid id,
        [FromRoute] Guid participantId,
        [FromBody] RevertPaymentDto dto)
    {
        var owner = HttpContext.GetOwner();

        // Verify split ownership
        var split = await _db.SplitSessions
            .FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == owner.Id);
        if (split is null) return NotFound("Split not found");

        // Get the latest split result
        var result = await _db.SplitResults
            .Include(r => r.Participants)
            .Where(r => r.SplitSessionId == id)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();
        if (result is null) return NotFound("Split not finalized");

        // Find the participant result
        var participantResult = result.Participants
            .FirstOrDefault(p => p.ParticipantId == participantId);
        if (participantResult is null) return NotFound("Participant not found");

        // Store old status for audit
        var oldStatus = participantResult.PaymentStatus;

        // Update payment status back to Unpaid
        participantResult.PaymentStatus = PaymentStatus.Unpaid;
        participantResult.PaymentUpdatedAt = DateTimeOffset.UtcNow;
        participantResult.PaymentSource = "owner-reverted";
        participantResult.PaymentNote = dto.Note;
        participantResult.VerifiedByUserId = null;

        // Create audit event
        var auditEvent = new PaymentStatusEvent
        {
            Id = Guid.NewGuid(),
            SplitResultId = result.Id,
            SplitParticipantResultId = participantResult.Id,
            OldStatus = oldStatus,
            NewStatus = PaymentStatus.Unpaid,
            Source = "owner-reverted",
            ActorId = owner.Id,
            Timestamp = DateTimeOffset.UtcNow,
            CorrelationId = Guid.NewGuid().ToString(),
            Notes = dto.Note
        };

        _db.PaymentStatusEvents.Add(auditEvent);
        await _db.SaveChangesAsync();

        // Emit real-time event
        var statusEvent = new PaymentStatusChangedEvent
        {
            SplitResultId = result.Id,
            ParticipantResultId = participantResult.Id,
            Status = PaymentStatus.Unpaid.ToString(),
            Source = "owner-reverted",
            Timestamp = DateTimeOffset.UtcNow,
            ParticipantName = participantResult.DisplayName,
            Amount = participantResult.Total
        };
        await _eventStream.EmitPaymentStatusChangedAsync(statusEvent);

        return NoContent();
    }
}
