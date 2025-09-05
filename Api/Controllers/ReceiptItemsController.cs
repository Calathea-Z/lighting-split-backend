// Api/Controllers/ReceiptItemsController.cs
using Api.Dtos.Receipts.Requests.Items;
using Api.Services.Receipts.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/receipts/{receiptId:guid}/items")]
public sealed class ReceiptItemsController(IReceiptItemsService items) : ControllerBase
{
    // POST /api/receipts/{receiptId}/items
    [HttpPost]
    public async Task<IActionResult> Create(Guid receiptId, [FromBody] CreateReceiptItemDto dto, CancellationToken ct = default)
    {
        var created = await items.AddItemAsync(receiptId, dto, ct);
        return created is null ? NotFound() : Ok(created);
    }

    // PUT /api/receipts/{receiptId}/items/{itemId}
    [HttpPut("{itemId:guid}")]
    public async Task<IActionResult> Update(Guid receiptId, Guid itemId, [FromBody] UpdateReceiptItemDto dto, CancellationToken ct = default)
    {
        try
        {
            var updated = await items.UpdateItemAsync(receiptId, itemId, dto, ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message); // concurrency clash
        }
    }

    // DELETE /api/receipts/{receiptId}/items/{itemId}?version=123
    [HttpDelete("{itemId:guid}")]
    public async Task<IActionResult> Delete(Guid receiptId, Guid itemId, [FromQuery] uint? version, CancellationToken ct = default)
    {
        if (version is null) return BadRequest("version is required to enforce concurrency.");
        try
        {
            var ok = await items.DeleteItemAsync(receiptId, itemId, version, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }
}
