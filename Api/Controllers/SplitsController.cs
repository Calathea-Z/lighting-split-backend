using Api.Data;
using Api.Dtos.Splits.Common;
using Api.Dtos.Splits.Requests;
using Api.Dtos.Splits.Responses;
using Api.Infrastructure.Middleware;
using Api.Models.Splits;
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

    public SplitsController(LightningDbContext db, ISplitCalculator calc, ISplitFinalizerService splitterFinalizerService, ISplitPaymentService splitPaymentService)
    {
        _db = db;
        _calc = calc;
        _splitterFinalizerService = splitterFinalizerService;
        _splitPaymentService = splitPaymentService;
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
}
