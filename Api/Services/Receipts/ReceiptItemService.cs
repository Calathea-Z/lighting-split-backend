using Api.Common.Interfaces;
using Api.Data;
using Api.Dtos.Receipts.Requests.Items;
using Api.Dtos.Receipts.Responses.Items;
using Api.Mappers;
using Api.Models;
using Api.Services.Receipts.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Api.Services.Receipts;

public sealed class ReceiptItemsService : IReceiptItemsService
{
    private readonly LightningDbContext _db;
    private readonly IClock _clock;
    private readonly IReceiptReconciliationOrchestrator _reconciler;

    public ReceiptItemsService(
        LightningDbContext db,
        IClock clock,
        IReceiptReconciliationOrchestrator reconciler)
    {
        _db = db;
        _clock = clock;
        _reconciler = reconciler;
    }

    public async Task<ReceiptItemDto?> AddItemAsync(Guid receiptId, CreateReceiptItemDto dto, CancellationToken ct = default)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        var exists = await _db.Receipts.AnyAsync(r => r.Id == receiptId, ct);
        if (!exists) return null;

        var item = new ReceiptItem
        {
            ReceiptId = receiptId,
            Label = (dto.Label ?? string.Empty).Trim(),
            Unit = dto.Unit,
            Category = dto.Category,
            Notes = dto.Notes,
            Position = dto.Position,
            Qty = NormalizeQty(dto.Qty),
            UnitPrice = Round2(dto.UnitPrice),
            Discount = dto.Discount is null ? null : Round2(dto.Discount.Value),
            Tax = dto.Tax is null ? null : Round2(dto.Tax.Value),
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow
        };
        ReceiptItemMaps.Recalculate(item);

        _db.ReceiptItems.Add(item);
        await _db.SaveChangesAsync(ct);

        // Single source of truth for header rollup + adjustment + status
        await _reconciler.ReconcileAsync(receiptId, ct);
        return item.ToDto();
    }

    public async Task<ReceiptItemDto?> UpdateItemAsync(Guid receiptId, Guid itemId, UpdateReceiptItemDto dto, CancellationToken ct = default)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        var item = await _db.ReceiptItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReceiptId == receiptId, ct);
        if (item is null) return null;

        // optimistic concurrency
        _db.Entry(item).Property(x => x.Version).OriginalValue = dto.Version;

        item.ApplyUpdate(dto);
        item.Label = (item.Label ?? string.Empty).Trim();
        item.Qty = NormalizeQty(item.Qty);
        item.UnitPrice = Round2(item.UnitPrice);
        item.Discount = item.Discount is null ? null : Round2(item.Discount.Value);
        item.Tax = item.Tax is null ? null : Round2(item.Tax.Value);
        item.UpdatedAt = _clock.UtcNow;

        // clamp discount
        var maxDiscount = Round2(item.Qty * item.UnitPrice);
        if (item.Discount is > 0m && item.Discount > maxDiscount) item.Discount = maxDiscount;

        ReceiptItemMaps.Recalculate(item);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Concurrency conflict. Reload the item and try again.");
        }

        await _reconciler.ReconcileAsync(receiptId, ct);
        return item.ToDto();
    }

    public async Task<bool> DeleteItemAsync(Guid receiptId, Guid itemId, uint? version, CancellationToken ct = default)
    {
        var item = await _db.ReceiptItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReceiptId == receiptId, ct);
        if (item is null) return false;

        if (version.HasValue)
            _db.Entry(item).Property(x => x.Version).OriginalValue = version.Value;

        _db.ReceiptItems.Remove(item);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Concurrency conflict while deleting the item.");
        }

        await _reconciler.ReconcileAsync(receiptId, ct);
        return true;
    }

    #region Helpers
    private static decimal NormalizeQty(decimal qty) =>
        qty <= 0m ? 1m : decimal.Round(qty, 3, MidpointRounding.AwayFromZero);

    private static decimal Round2(decimal v) =>
        decimal.Round(v, 2, MidpointRounding.AwayFromZero);
    #endregion
}
