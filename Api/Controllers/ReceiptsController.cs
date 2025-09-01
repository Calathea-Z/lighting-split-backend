using Api.Dtos;
using Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public sealed class ReceiptsController(IReceiptService receipts) : ControllerBase
{
    // POST /api/receipts
    [HttpPost]
    [ProducesResponseType(typeof(ReceiptSummaryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReceiptSummaryDto>> Create(
        [FromBody] CreateReceiptDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var result = await receipts.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // GET /api/receipts/{id}
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ReceiptDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReceiptDetailDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await receipts.GetByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // GET /api/receipts?ownerUserId=...&skip=0&take=50
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ReceiptSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ReceiptSummaryDto>>> List(
        [FromQuery] string? ownerUserId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var rows = await receipts.ListAsync(ownerUserId, skip, take, ct);
        return Ok(rows);
    }

    // DELETE /api/receipts/{id}
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var ok = await receipts.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    // PATCH /api/receipts/{id}/totals
    // Allows parser or user to update money fields (nullable-friendly)
    [HttpPatch("{id:guid}/totals")]
    [ProducesResponseType(typeof(ReceiptSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateTotals(
        Guid id,
        [FromBody] UpdateTotalsDto dto,
        CancellationToken ct = default)
    {
        var result = await receipts.UpdateTotalsAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // POST /api/receipts/upload
    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]               // 20 MB
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ReceiptSummaryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReceiptSummaryDto>> Upload(
        [FromForm] UploadReceiptItemDto form,
        CancellationToken ct = default)
    {
        try
        {
            var dto = await receipts.UploadAsync(form, ct);
            return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // Optional: endpoint for the Function to report parse failures (recommended)
    [HttpPost("{id:guid}/parse-error")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkParseFailed(Guid id, [FromBody] string error, CancellationToken ct = default)
    {
        var r = await receipts.GetByIdAsync(id, ct);
        if (r is null) return NotFound();

        // If you have a service method for this, use it; otherwise quick inline:
        // (You can move this into ReceiptService later.)
        return Ok();
    }
}
