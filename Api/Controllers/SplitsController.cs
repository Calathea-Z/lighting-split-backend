using Api.Data;
using Api.Dtos.Splits.Requests;
using Api.Dtos.Splits.Responses;
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
    private readonly IAokService _aok;
    private readonly ISplitCalculator _calc;
    private readonly ISplitFinalizerService _splitterFinalizerService;
    private readonly ISplitPaymentService _splitPaymentService;

    public SplitsController(LightningDbContext db, IAokService aok, ISplitCalculator calc, ISplitFinalizerService splitterFinalizerService, ISplitPaymentService splitPaymentService)
    {
        _db = db; 
        _aok = aok; 
        _calc = calc;
        _splitterFinalizerService = splitterFinalizerService;
        _splitPaymentService = splitPaymentService;
    }

    [HttpGet("{id:guid}/preview")]
    public async Task<ActionResult<SplitPreviewDto>> Preview([FromRoute] Guid id)
    {
        var owner = await _aok.ResolveOwnerAsync(HttpContext);
        if (owner is null) return Unauthorized();

        // ensure the split belongs to owner
        var own = await _db.SplitSessions.AnyAsync(s => s.Id == id && s.OwnerId == owner.Id);
        if (!own) return NotFound();

        var dto = await _calc.PreviewAsync(id);
        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<CreateSplitResponseDto>> Create([FromBody] CreateSplitDto req)
    {
        var owner = await _aok.ResolveOwnerAsync(HttpContext);
        if (owner is null) return Unauthorized();

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

    [HttpPost("{id:guid}/participants")]
    public async Task<IActionResult> AddParticipant([FromRoute] Guid id, [FromBody] CreateSplitParticipantDto req)
    {
        var owner = await _aok.ResolveOwnerAsync(HttpContext);
        if (owner is null) return Unauthorized();

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

    [HttpPost("{id:guid}/claims")]
    public async Task<IActionResult> UpsertClaims([FromRoute] Guid id, [FromQuery] bool replace, [FromBody] UpsertItemClaimsDto req)
    {
        var owner = await _aok.ResolveOwnerAsync(HttpContext);
        if (owner is null) return Unauthorized();

        var s = await _db.SplitSessions.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == owner.Id);
        if (s is null) return NotFound();

        // Validate participants belong to split & items belong to receipt
        var partIds = await _db.SplitParticipants
            .Where(p => p.SplitSessionId == s.Id)
            .Select(p => p.Id)
            .ToHashSetAsync();

        var itemIds = await _db.ReceiptItems
            .Where(i => i.ReceiptId == s.ReceiptId)
            .Select(i => i.Id)
            .ToHashSetAsync();

        foreach (var c in req.Claims)
        {
            if (!partIds.Contains(c.ParticipantId)) return BadRequest("Participant not in split.");
            if (!itemIds.Contains(c.ReceiptItemId)) return BadRequest("Item not in receipt.");
            if (c.QtyShare < 0m) return BadRequest("QtyShare must be >= 0.");
        }

        if (replace)
        {
            var existing = await _db.ItemClaims.Where(x => x.SplitSessionId == s.Id).ToListAsync();
            _db.RemoveRange(existing);
        }

        // Upsert per (item, participant)
        var keys = req.Claims.Select(c => new { c.ReceiptItemId, c.ParticipantId }).ToList();
        var existingMap = await _db.ItemClaims
            .Where(x => x.SplitSessionId == s.Id && keys.Contains(new { x.ReceiptItemId, x.ParticipantId }))
            .ToDictionaryAsync(x => (x.ReceiptItemId, x.ParticipantId));

        foreach (var c in req.Claims)
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

    [HttpPost("{id:guid}/finalize")]
    public async Task<ActionResult<FinalizeSplitResponse>> Finalize([FromRoute] Guid id)
    {
        var owner = await _aok.ResolveOwnerAsync(HttpContext);
        if (owner is null) return Unauthorized();

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var dto = await _splitterFinalizerService.FinalizeAsync(id, owner.Id, baseUrl);
        return Ok(dto);
    }


    [HttpPatch("{id:guid}/participants/{participantId:guid}/payment")]
    public async Task<IActionResult> SetPayment(
        [FromRoute] Guid id,
        [FromRoute] Guid participantId,
        [FromBody] SetPaymentDto dto)
    {
        var owner = await _aok.ResolveOwnerAsync(HttpContext);
        if (owner is null) return Unauthorized();

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
