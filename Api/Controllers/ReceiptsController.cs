using Api.Dtos;
using Api.Data;
using Api.Mapping;
using Api.Models;          // <- contains Receipt + ReceiptStatus
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Api.Storage;
using Api.Enums;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReceiptsController(LightningDbContext db, IFileStorage fileStorage) : ControllerBase
{
    // POST /api/receipts/upload
    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)] // 20 MB
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ReceiptSummaryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReceiptSummaryDto>> Upload([FromForm] UploadReceiptRequestDto form, CancellationToken ct)
    {
        var file = form.File;
        if (file is null || file.Length == 0) return BadRequest("File is required.");

        StoredFile stored;
        try
        {
            stored = await fileStorage.SaveAsync(file, ct: ct);
        }
        catch (InvalidOperationException ex)
        {
            // from storage guardrails: size, type, etc.
            return BadRequest(ex.Message);
        }

        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            OwnerUserId = null, // anonymous for now
            Status = ReceiptStatus.PendingParse,
            OriginalFileUrl = stored.Url,
            SubTotal = 0m,
            Tax = 0m,
            Tip = 0m,
            Total = 0m,
            Items = []
        };

        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(ct);

        var dto = receipt.ToSummaryDto();
        return CreatedAtAction(nameof(GetById), new { id = receipt.Id }, dto);
    }

    // POST /api/receipts
    [HttpPost]
    public async Task<ActionResult<ReceiptSummaryDto>> Create([FromBody] CreateReceiptDto dto)
    {
        if (dto is null) return BadRequest("Body is required.");
        if (dto.Items is null || dto.Items.Count == 0)
            return BadRequest("At least one item is required.");

        // basic guardrails
        if (dto.Items.Any(i => i.Qty <= 0 || i.UnitPrice < 0))
            return BadRequest("Item quantities must be > 0 and prices must be >= 0.");

        var entity = dto.ToEntity();

        db.Receipts.Add(entity);
        await db.SaveChangesAsync();

        var result = entity.ToSummaryDto();
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
    }

    // GET /api/receipts/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReceiptDetailDto>> GetById(Guid id)
    {
        var receipt = await db.Receipts
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id);

        return receipt is null ? NotFound() : Ok(receipt.ToDetailDto());
    }

    // GET /api/receipts?ownerUserId=...&skip=0&take=50
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReceiptSummaryDto>>> List(
        [FromQuery] string? ownerUserId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 200);

        var query = db.Receipts.AsNoTracking().Include(r => r.Items)
            .OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrWhiteSpace(ownerUserId))
            query = query.Where(r => r.OwnerUserId == ownerUserId)
                         .OrderByDescending(r => r.CreatedAt);

        var rows = await query
            .Skip(skip).Take(take)
            .Select(r => r.ToSummaryDto())
            .ToListAsync();

        return Ok(rows);
    }

    // DELETE /api/receipts/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var entity = await db.Receipts.FindAsync(id);
        if (entity is null) return NotFound();

        db.Receipts.Remove(entity);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // PATCH /api/receipts/{id}/totals  (optional: allow parser to update money fields)
    [HttpPatch("{id:guid}/totals")]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateTotals(Guid id, [FromBody] UpdateTotalsDto dto)
    {
        var entity = await db.Receipts.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id);
        if (entity is null) return NotFound();

        entity.SubTotal = dto.SubTotal ?? entity.Items.Sum(i => i.UnitPrice * i.Qty);
        entity.Tax = dto.Tax;
        entity.Tip = dto.Tip;
        entity.Total = dto.Total ?? (entity.SubTotal ?? 0m) + (dto.Tax ?? entity.Tax ?? 0m) + (dto.Tip ?? entity.Tip ?? 0m);

        await db.SaveChangesAsync();
        return Ok(entity.ToSummaryDto());
    }
}
