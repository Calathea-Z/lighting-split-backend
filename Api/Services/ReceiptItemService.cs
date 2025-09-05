using Api.Common.Interfaces;
using Api.Data;
using Api.Dtos.Receipts.Requests.Items;
using Api.Dtos.Receipts.Responses.Items;
using Api.Interfaces;
using Api.Mappers;
using Api.Models;
using Microsoft.EntityFrameworkCore;

public sealed class ReceiptItemsService : IReceiptItemsService
{
    private readonly LightningDbContext _db;
    private readonly IClock _clock;
    private readonly IReconciliationService _reconciliationService;

    public ReceiptItemsService(LightningDbContext db, IClock clock, IReconciliationService reconciliationService)
    {
        _db = db;
        _clock = clock;
        _reconciliationService = reconciliationService;
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

        await UpdateHeaderAndReconcileAsync(receiptId, ct);
        return item.ToDto();
    }

    public async Task<ReceiptItemDto?> UpdateItemAsync(Guid receiptId, Guid itemId, UpdateReceiptItemDto dto, CancellationToken ct = default)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        var item = await _db.ReceiptItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReceiptId == receiptId, ct);
        if (item is null) return null;

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

        // touch header timestamp without loading it
        await _db.Receipts
            .Where(r => r.Id == receiptId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UpdatedAt, _ => _clock.UtcNow), ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Concurrency conflict. Reload the item and try again.");
        }

        await UpdateHeaderAndReconcileAsync(receiptId, ct);
        return item.ToDto();
    }

    public async Task<bool> DeleteItemAsync(Guid receiptId, Guid itemId, uint? version, CancellationToken ct = default)
    {
        var item = await _db.ReceiptItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReceiptId == receiptId, ct);
        if (item is null) return false;

        if (version.HasValue)
            _db.Entry(item).Property(x => x.Version).OriginalValue = version.Value;

        _db.ReceiptItems.Remove(item);

        await _db.Receipts
            .Where(r => r.Id == receiptId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.UpdatedAt, _ => _clock.UtcNow), ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Concurrency conflict while deleting the item.");
        }

        await UpdateHeaderAndReconcileAsync(receiptId, ct);
        return true;
    }

    #region Helpers
    private static decimal NormalizeQty(decimal qty) =>
        qty <= 0m ? 1m : decimal.Round(qty, 3, MidpointRounding.AwayFromZero);

    private static decimal Round2(decimal v) =>
        decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Recompute header from items (set-based), then run reconciliation including Adjustment upsert.
    /// </summary>
    private async Task UpdateHeaderAndReconcileAsync(Guid receiptId, CancellationToken ct)
    {
        // Aggregate items in SQL
        var agg = await _db.ReceiptItems
            .Where(x => x.ReceiptId == receiptId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Sub = g.Sum(x => x.LineSubtotal),
                Tax = g.Sum(x => x.Tax ?? 0m),
                Tot = g.Sum(x => x.LineTotal)
            })
            .SingleOrDefaultAsync(ct);

        // Update header in one shot
        await _db.Receipts
            .Where(r => r.Id == receiptId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SubTotal, _ => agg == null ? (decimal?)null : Round2(agg.Sub))
                .SetProperty(r => r.Tax, _ => agg == null || agg.Tax == 0m ? (decimal?)null : Round2(agg.Tax))
                .SetProperty(r => r.Total, _ => agg == null ? (decimal?)null : Round2(agg.Tot))
                .SetProperty(r => r.UpdatedAt, _ => _clock.UtcNow), ct);

        // Reconciliation (needs items loaded)
        var r = await _db.Receipts.Include(x => x.Items).FirstAsync(x => x.Id == receiptId, ct);

        var parsed = BuildParsedReceipt(r);
        var result = _reconciliationService.Reconcile(parsed);

        r.ComputedItemsSubtotal = result.ItemsSum;
        r.BaselineSubtotal = result.BaselineSubtotal;
        r.Discrepancy = result.Discrepancy;
        r.Reason = result.Reason;

        r.Status = result.Status == ParseStatus.Parsed ? "Parsed" : "ParsedNeedsReview";
        r.NeedsReview = r.Status == "ParsedNeedsReview";
        r.UpdatedAt = _clock.UtcNow;

        await UpsertAdjustmentAsync(r, result, ct);
        await _db.SaveChangesAsync(ct);
    }

    private static ParsedReceipt BuildParsedReceipt(Receipt r)
    {
        var items = r.Items
            .Where(i => !i.IsSystemGenerated)
            .Select(i => new ParsedItem(
                Description: i.Label ?? string.Empty,
                Qty: (int)Math.Round(i.Qty <= 0 ? 1m : i.Qty, MidpointRounding.AwayFromZero),
                UnitPrice: decimal.Round(i.UnitPrice, 2, MidpointRounding.AwayFromZero)
            ))
            .ToList();

        var totals = new ParsedMoneyTotals(
            Subtotal: r.SubTotal,
            Tax: r.Tax,
            Tip: r.Tip,
            Total: r.Total
        );

        return new ParsedReceipt(items, totals, r.RawText ?? string.Empty);
    }

    private async Task UpsertAdjustmentAsync(Receipt r, ReconcileResult result, CancellationToken ct)
    {
        var adj = r.Items.FirstOrDefault(x => x.IsSystemGenerated && x.Label == "Adjustment");

        if (!result.NeedsAdjustment)
        {
            if (adj is not null)
            {
                _db.ReceiptItems.Remove(adj);
                await _db.SaveChangesAsync(ct);
            }
            return;
        }

        var delta = decimal.Round(result.BaselineSubtotal - result.ItemsSum, 2, MidpointRounding.AwayFromZero);

        if (adj is null)
        {
            adj = new ReceiptItem
            {
                ReceiptId = r.Id,
                Label = "Adjustment",
                IsSystemGenerated = true,
                Qty = 1,
                UnitPrice = delta,
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow
            };
            ReceiptItemMaps.Recalculate(adj);
            _db.ReceiptItems.Add(adj);
        }
        else
        {
            adj.UnitPrice = delta;
            adj.UpdatedAt = _clock.UtcNow;
            ReceiptItemMaps.Recalculate(adj);
            _db.ReceiptItems.Update(adj);
        }

        // Keep header totals consistent after Adjustment (set-based)
        var agg = await _db.ReceiptItems
            .Where(x => x.ReceiptId == r.Id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Sub = g.Sum(x => x.LineSubtotal),
                Tax = g.Sum(x => x.Tax ?? 0m),
                Tot = g.Sum(x => x.LineTotal)
            })
            .SingleOrDefaultAsync(ct);

        await _db.Receipts
            .Where(x => x.Id == r.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SubTotal, _ => agg == null ? (decimal?)null : Round2(agg.Sub))
                .SetProperty(x => x.Tax, _ => agg == null || agg.Tax == 0m ? (decimal?)null : Round2(agg.Tax))
                .SetProperty(x => x.Total, _ => agg == null ? (decimal?)null : Round2(agg.Tot))
                .SetProperty(x => x.UpdatedAt, _ => _clock.UtcNow), ct);
    }
    #endregion
}
