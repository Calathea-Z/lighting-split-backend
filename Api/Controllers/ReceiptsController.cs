using Api.Dtos;
using Api.Interfaces;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public sealed class ReceiptsController(IReceiptService receipts) : ControllerBase
{

    #region GET
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
    #endregion

    #region POST
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

    // POST /api/receipts/{id:guid}/parse-error
    [HttpPost("{id:guid}/parse-error")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkParseFailed(Guid id, [FromBody] string error, CancellationToken ct = default)
    {
        var ok = await receipts.MarkParseFailedAsync(id, error, ct);
        return ok ? Ok() : NotFound();
    }

    // POST /api/receipts/{receiptId}/items
    [HttpPost("{receiptId:guid}/items")]
    [ProducesResponseType(typeof(ReceiptItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReceiptItemDto>> AddItem(Guid receiptId, [FromBody] CreateReceiptItemDto dto, CancellationToken ct = default)
    {
        var item = await receipts.AddItemAsync(receiptId, dto, ct);
        return item is null ? NotFound() : CreatedAtAction(nameof(GetById), new { id = receiptId }, item);
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
    #endregion

    #region PATCH
    // PATCH /api/receipts/{id}/totals
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

    // PATCH /api/receipts/{receiptId}/items/{itemId}
    [HttpPatch("{receiptId:guid}/items/{itemId:guid}")]
    [ProducesResponseType(typeof(ReceiptItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReceiptItemDto>> UpdateItem(Guid receiptId, Guid itemId, [FromBody] UpdateReceiptItemDto dto, CancellationToken ct = default)
    {
        try
        {
            var item = await receipts.UpdateItemAsync(receiptId, itemId, dto, ct);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Concurrency"))
        {
            return Conflict(ex.Message); // 409
        }
    }
    #endregion

    #region DELETE
    // DELETE /api/receipts/{id}
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var ok = await receipts.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    // DELETE /api/receipts/{receiptId}/items/{itemId}?version=123
    [HttpDelete("{receiptId:guid}/items/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteItem(Guid receiptId, Guid itemId, [FromQuery] uint? version, CancellationToken ct = default)
    {
        try
        {
            var ok = await receipts.DeleteItemAsync(receiptId, itemId, version, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Concurrency"))
        {
            return Conflict(ex.Message);
        }
    }
    #endregion
}
