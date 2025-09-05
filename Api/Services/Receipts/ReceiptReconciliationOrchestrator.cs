using Api.Abstractions.Receipts;
using Api.Common.Interfaces;
using Api.Contracts.Reconciliation;
using Api.Data;
using Api.Mappers;
using Api.Models;
using Api.Services.Receipts.Abstractions;
using Api.Services.Reconciliation.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Api.Services.Receipts
{
    public sealed class ReceiptReconciliationOrchestrator(
        LightningDbContext db,
        IReceiptReconciliationCalculator reconciliation,
        IClock clock
    ) : IReceiptReconciliationOrchestrator
    {
        public async Task ReconcileAsync(Guid receiptId, CancellationToken ct = default)
        {
            // 1) make sure header matches current items (SQL aggregate)
            await RecomputeHeaderAsync(receiptId, ct);

            // 2) load receipt + items for reconciliation
            var r = await db.Receipts
                .Include(x => x.Items)
                .FirstAsync(x => x.Id == receiptId, ct);

            var parsed = BuildParsedReceipt(r);
            var result = reconciliation.Reconcile(parsed);

            // 3) persist transparency + status
            r.ComputedItemsSubtotal = result.ItemsSum;
            r.BaselineSubtotal = result.BaselineSubtotal;
            r.Discrepancy = result.Discrepancy;
            r.Reason = result.Reason;

            r.Status = result.Status == ParseStatus.Success
                ? ReceiptStatus.Parsed
                : ReceiptStatus.ParsedNeedsReview;

            r.NeedsReview = r.Status == ReceiptStatus.ParsedNeedsReview;
            r.UpdatedAt = clock.UtcNow;

            // 4) upsert Adjustment line if needed
            await UpsertAdjustment(r, result, ct);
            await db.SaveChangesAsync(ct);

            // 5) adjustment changed items → recompute header again
            await RecomputeHeaderAsync(receiptId, ct);
        }

        // --- helpers ---
        private async Task RecomputeHeaderAsync(Guid receiptId, CancellationToken ct)
        {
            var agg = await db.ReceiptItems
                .Where(x => x.ReceiptId == receiptId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Sub = g.Sum(x => x.LineSubtotal),
                    Tax = g.Sum(x => x.Tax ?? 0m),
                    Tot = g.Sum(x => x.LineTotal)
                })
                .SingleOrDefaultAsync(ct);

            await db.Receipts.Where(r => r.Id == receiptId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.SubTotal, _ => agg == null ? (decimal?)null : Round2(agg.Sub))
                    .SetProperty(r => r.Tax, _ => agg == null || agg.Tax == 0m ? (decimal?)null : Round2(agg.Tax))
                    .SetProperty(r => r.Total, _ => agg == null ? (decimal?)null : Round2(agg.Tot))
                    .SetProperty(r => r.UpdatedAt, _ => clock.UtcNow), ct);
        }

        private static ParsedReceipt BuildParsedReceipt(Receipt r)
        {
            var items = r.Items
                .Where(i => !i.IsSystemGenerated)
                .Select(i => new ParsedItem(
                    Description: i.Label ?? string.Empty,
                    Qty: (int)Math.Round(i.Qty <= 0 ? 1m : i.Qty, MidpointRounding.AwayFromZero),
                    UnitPrice: decimal.Round(i.UnitPrice, 2, MidpointRounding.AwayFromZero)))
                .ToList();

            var totals = new ParsedMoneyTotals(
                Subtotal: r.SubTotal,
                Tax: r.Tax,
                Tip: r.Tip,
                Total: r.Total);

            return new ParsedReceipt(items, totals, r.RawText ?? string.Empty);
        }

        private Task UpsertAdjustment(Receipt r, ReconcileResult result, CancellationToken ct)
        {
            var adjustment = r.Items.FirstOrDefault(x => x.IsSystemGenerated && x.Label == "Adjustment");

            if (!result.NeedsAdjustment)
            {
                if (adjustment is not null) db.ReceiptItems.Remove(adjustment);
                return Task.CompletedTask;
            }

            var delta = decimal.Round(result.BaselineSubtotal - result.ItemsSum, 2, MidpointRounding.AwayFromZero);

            if (adjustment is null)
            {
                adjustment = new ReceiptItem
                {
                    ReceiptId = r.Id,
                    Label = "Adjustment",
                    IsSystemGenerated = true,
                    Qty = 1,
                    UnitPrice = delta,
                    CreatedAt = clock.UtcNow,
                    UpdatedAt = clock.UtcNow
                };
                ReceiptItemMaps.Recalculate(adjustment);
                db.ReceiptItems.Add(adjustment);
            }
            else
            {
                adjustment.UnitPrice = delta;
                adjustment.UpdatedAt = clock.UtcNow;
                ReceiptItemMaps.Recalculate(adjustment);
                db.ReceiptItems.Update(adjustment);
            }

            return Task.CompletedTask;
        }

        private static decimal Round2(decimal v) =>
            decimal.Round(v, 2, MidpointRounding.AwayFromZero);
    }
}
