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

        // Block user-created "Adjustment" rows (system-managed)
        var label = (dto.Label ?? string.Empty).Trim();
        if (string.Equals(label, "Adjustment", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The 'Adjustment' line is system-managed and cannot be created manually.");

        // Compute next position: if dto.Position < 1, use max+1
        var maxPos = await _db.ReceiptItems
            .Where(x => x.ReceiptId == receiptId)
            .Select(x => (int?)x.Position)
            .MaxAsync(ct);

        int nextPos = (dto.Position >= 1) ? dto.Position : ((maxPos ?? 0) + 1);

        var item = new ReceiptItem
        {
            ReceiptId = receiptId,
            Label = label,
            Unit = string.IsNullOrWhiteSpace(dto.Unit) ? null : dto.Unit.Trim(),
            Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            Position = nextPos,
            Qty = NormalizeQty(dto.Qty),
            UnitPrice = Round2(dto.UnitPrice),
            Discount = dto.Discount is null ? null : Round2(dto.Discount.Value),
            Tax = dto.Tax is null ? null : Round2(dto.Tax.Value),
            IsSystemGenerated = false,
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow
        };

        // Clamp discount ≤ (qty * unit)
        var maxDiscount = Round2(item.Qty * item.UnitPrice);
        if (item.Discount is > 0m && item.Discount > maxDiscount) item.Discount = maxDiscount;

        ReceiptItemMaps.Recalculate(item);

        _db.ReceiptItems.Add(item);
        await _db.SaveChangesAsync(ct);

        // Keep header/adjustment/status consistent
        await _reconciler.ReconcileAsync(receiptId, ct);

        return item.ToDto();
    }


    public async Task<ReceiptItemDto?> UpdateItemAsync(Guid receiptId, Guid itemId, UpdateReceiptItemDto dto, CancellationToken ct = default)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        var item = await _db.ReceiptItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReceiptId == receiptId, ct);
        if (item is null) return null;

        // Block manual edits of system "Adjustment"
        if (item.IsSystemGenerated && item.Label == "Adjustment")
            throw new InvalidOperationException("System-generated Adjustment cannot be modified manually.");

        // optimistic concurrency
        _db.Entry(item).Property(x => x.Version).OriginalValue = dto.Version;

        // Apply incoming changes
        item.ApplyUpdate(dto);

        // Normalize strings
        item.Label = (item.Label ?? string.Empty).Trim();
        item.Unit = string.IsNullOrWhiteSpace(item.Unit) ? null : item.Unit.Trim();
        item.Category = string.IsNullOrWhiteSpace(item.Category) ? null : item.Category.Trim();
        item.Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim();

        // Position: if API allows changing it, keep existing value if missing
        if (dto.Position.HasValue) item.Position = dto.Position.Value;

        // Normalize numbers
        item.Qty = NormalizeQty(item.Qty);
        item.UnitPrice = Round2(item.UnitPrice);
        item.Discount = item.Discount is null ? null : Round2(item.Discount.Value);
        item.Tax = item.Tax is null ? null : Round2(item.Tax.Value);
        item.UpdatedAt = _clock.UtcNow;

        // Clamp discount ≤ line subtotal
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

        // Block deletes of system "Adjustment"
        if (item.IsSystemGenerated && item.Label == "Adjustment")
            throw new InvalidOperationException("System-generated Adjustment cannot be deleted manually.");

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
