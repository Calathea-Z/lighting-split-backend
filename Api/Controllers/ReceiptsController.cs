// Api/Controllers/ReceiptsController.cs
using Api.Dtos.Receipts.Requests;
using Api.Dtos.Receipts.Responses;
using Api.Dtos.Receipts.Responses.Items;
using Api.Services.Receipts.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ReceiptsController(IReceiptService receiptService) : ControllerBase
{
    // GET /api/receipts/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReceiptDetailDto>> GetById([FromRoute] Guid id, CancellationToken ct = default)
    {
        var result = await receiptService.GetByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // GET /api/receipts?ownerUserId=...&skip=0&take=50
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReceiptSummaryDto>>> List(
        [FromQuery] string? ownerUserId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (skip < 0) return BadRequest("skip must be >= 0");
        if (take <= 0 || take > 200) return BadRequest("take must be between 1 and 200");

        var rows = await receiptService.ListAsync(ownerUserId, skip, take, ct);
        return Ok(rows);
    }

    // POST /api/receipts
    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> Create([FromBody] CreateReceiptDto dto, CancellationToken ct = default)
    {
        try
        {
            var result = await receiptService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    // POST /api/receipts/{id}/parse-error
    [HttpPost("{id:guid}/parse-error")]
    public async Task<IActionResult> MarkParseFailed([FromRoute] Guid id, [FromBody] string error, CancellationToken ct = default)
    {
        var ok = await receiptService.MarkParseFailedAsync(id, error, ct);
        return ok ? Ok() : NotFound();
    }

    // POST /api/receipts/upload
    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ReceiptSummaryDto>> Upload([FromForm] UploadReceiptItemDto form, CancellationToken ct = default)
    {
        try
        {
            var dto = await receiptService.UploadAsync(form, ct);
            return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }

    // PATCH /api/receipts/{id}/totals
    [HttpPatch("{id:guid}/totals")]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateTotals([FromRoute] Guid id, [FromBody] UpdateTotalsDto dto, CancellationToken ct = default)
    {
        var result = await receiptService.UpdateTotalsAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // PATCH /api/receipts/{id}/rawtext
    [HttpPatch("{id:guid}/rawtext")]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateRawText([FromRoute] Guid id, [FromBody] UpdateRawTextDto dto, CancellationToken ct = default)
    {
        var result = await receiptService.UpdateRawTextAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // PATCH /api/receipts/{id}/status
    [HttpPatch("{id:guid}/status")]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateStatus([FromRoute] Guid id, [FromBody] UpdateStatusDto dto, CancellationToken ct = default)
    {
        var result = await receiptService.UpdateStatusAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // PATCH /api/receipts/{id}/review
    [HttpPatch("{id:guid}/review")]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateReview([FromRoute] Guid id, [FromBody] UpdateReviewDto dto, CancellationToken ct = default)
    {
        var result = await receiptService.UpdateReviewAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // DELETE /api/receipts/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct = default)
    {
        try
        {
            var ok = await receiptService.DeleteAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
    }
}
