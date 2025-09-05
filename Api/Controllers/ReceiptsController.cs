using Api.Dtos.Receipts.Requests;
using Api.Dtos.Receipts.Requests.Items;
using Api.Dtos.Receipts.Responses;
using Api.Dtos.Receipts.Responses.Items;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public sealed class ReceiptsController(IReceiptService receipts) : ControllerBase
{
    #region GET

    /// <summary>
    /// GET /api/receipts/{id}
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReceiptDetailDto>> GetById([FromRoute] Guid id, CancellationToken ct = default)
    {
        var result = await receipts.GetByIdAsync(id, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// GET /api/receipts?ownerUserId=...&skip=0&take=50
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReceiptSummaryDto>>> List(
        [FromQuery] string? ownerUserId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        if (skip < 0) return BadRequest("skip must be >= 0");
        if (take <= 0 || take > 200) return BadRequest("take must be between 1 and 200");

        var rows = await receipts.ListAsync(ownerUserId, skip, take, ct);
        return Ok(rows);
    }
    #endregion

    #region POST

    /// <summary>
    /// POST /api/receipts
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> Create([FromBody] CreateReceiptDto dto, CancellationToken ct = default)
    {
        try
        {
            var result = await receipts.CreateAsync(dto, ct);
            if (result is null) return BadRequest("Creation failed.");
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// POST /api/receipts/{id}/parse-error
    /// </summary>
    [HttpPost("{id:guid}/parse-error")]
    public async Task<IActionResult> MarkParseFailed([FromRoute] Guid id, [FromBody] string error, CancellationToken ct = default)
    {
        var ok = await receipts.MarkParseFailedAsync(id, error, ct);
        return ok ? Ok() : NotFound();
    }

    /// <summary>
    /// POST /api/receipts/{receiptId}/items
    /// </summary>
    [HttpPost("{receiptId:guid}/items")]
    public async Task<ActionResult<ReceiptItemDto>> AddItem([FromRoute] Guid receiptId, [FromBody] CreateReceiptItemDto dto, CancellationToken ct = default)
    {
        var item = await receipts.AddItemAsync(receiptId, dto, ct);
        return item is null ? NotFound() : CreatedAtAction(nameof(GetById), new { id = receiptId }, item);
    }

    /// <summary>
    /// POST /api/receipts/upload
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)] // 20 MB
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ReceiptSummaryDto>> Upload([FromForm] UploadReceiptItemDto form, CancellationToken ct = default)
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
    #endregion

    #region PATCH

    /// <summary>
    /// PATCH /api/receipts/{id}/totals
    /// </summary>
    [HttpPatch("{id:guid}/totals")]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateTotals([FromRoute] Guid id, [FromBody] UpdateTotalsDto dto, CancellationToken ct = default)
    {
        var result = await receipts.UpdateTotalsAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// PATCH /api/receipts/{receiptId}/items/{itemId}
    /// </summary>
    [HttpPatch("{receiptId:guid}/items/{itemId:guid}")]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptItemDto>> UpdateItem(
        [FromRoute] Guid receiptId,
        [FromRoute] Guid itemId,
        [FromBody] UpdateReceiptItemDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var item = await receipts.UpdateItemAsync(receiptId, itemId, dto, ct);
            return item is null ? NotFound() : Ok(item);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Concurrency", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(ex.Message); // 409 for optimistic concurrency
        }
    }

    /// <summary>
    /// PATCH /api/receipts/{id}/rawtext
    /// </summary>
    [HttpPatch("{id:guid}/rawtext")]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateRawText([FromRoute] Guid id, [FromBody] UpdateRawTextDto dto, CancellationToken ct = default)
    {
        var result = await receipts.UpdateRawTextAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// PATCH /api/receipts/{id}/status
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateStatus([FromRoute] Guid id, [FromBody] UpdateStatusDto dto, CancellationToken ct = default)
    {
        var result = await receipts.UpdateStatusAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// PATCH /api/receipts/{id}/review
    /// </summary>
    [HttpPatch("{id:guid}/review")]
    [Consumes("application/json")]
    public async Task<ActionResult<ReceiptSummaryDto>> UpdateReview([FromRoute] Guid id, [FromBody] UpdateReviewDto dto, CancellationToken ct = default)
    {
        var result = await receipts.UpdateReviewAsync(id, dto, ct);
        return result is null ? NotFound() : Ok(result);
    }
    #endregion

    #region DELETE

    /// <summary>
    /// DELETE /api/receipts/{id}
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct = default)
    {
        try
        {
            var ok = await receipts.DeleteAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// DELETE /api/receipts/{receiptId}/items/{itemId}?version=123
    /// </summary>
    [HttpDelete("{receiptId:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> DeleteItem(
        [FromRoute] Guid receiptId,
        [FromRoute] Guid itemId,
        [FromQuery] uint? version,
        CancellationToken ct = default)
    {
        if (version is null) return BadRequest("version is required for delete to enforce concurrency.");

        try
        {
            var ok = await receipts.DeleteItemAsync(receiptId, itemId, version, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Concurrency", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
    #endregion
}
