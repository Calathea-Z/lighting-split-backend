using Api.Abstractions.Receipts;
using Api.Common.Interfaces;
using Api.Contracts.Reconciliation;
using Api.Data;
using Api.Mappers;
using Api.Models;
using Api.Models.Receipts;
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
        private const string AutoAdjLabel = "Adjustment";
        private const string AutoAdjNote = "Auto-reconcile";

        public async Task ReconcileAsync(Guid receiptId, CancellationToken ct = default)
        {
            // 1) Load receipt + items (preserve OCR totals)
            var r = await db.Receipts
                .Include(x => x.Items)
                .FirstAsync(x => x.Id == receiptId, ct);

            var isMidParse = r.Status == ReceiptStatus.PendingParse;

            // 2) Reconcile using current items + printed totals
            var parsed = BuildParsedReceipt(r); // excludes system Adjustment from math
            var result = reconciliation.Reconcile(parsed);

            // 3) Persist transparency fields (always)
            r.ComputedItemsSubtotal = result.ItemsSum;
            r.BaselineSubtotal = result.BaselineSubtotal;
            r.Discrepancy = result.Discrepancy;
            r.Reason = result.Reason;
            r.UpdatedAt = clock.UtcNow;

            if (isMidParse)
            {
                // While parsing: keep PendingParse, do NOT create/keep adjustments
                r.Status = ReceiptStatus.PendingParse;
                r.NeedsReview = false;

                // Remove any stale system Adjustment created by earlier runs
                var staleAdj = r.Items
                    .Where(x => x.IsSystemGenerated &&
                                string.Equals(x.Label, AutoAdjLabel, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (staleAdj.Count > 0)
                    db.ReceiptItems.RemoveRange(staleAdj);

                await db.SaveChangesAsync(ct);
                return;
            }

            // 4) After parsing is complete: set status
            r.Status = result.Status == ParseStatus.Success
                ? ReceiptStatus.Parsed
                : ReceiptStatus.ParsedNeedsReview;
            r.NeedsReview = r.Status == ReceiptStatus.ParsedNeedsReview;

            // Only keep/create auto Adjustment when fully Parsed
            var allowAutoAdjust = r.Status == ReceiptStatus.Parsed;

            if (!allowAutoAdjust)
            {
                // Remove any system adjustment so discrepancy remains visible
                var autos = r.Items
                    .Where(x => x.IsSystemGenerated &&
                                string.Equals(x.Label, AutoAdjLabel, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (autos.Count > 0)
                    db.ReceiptItems.RemoveRange(autos);
            }
            else
            {
                await UpsertAdjustment(r, result, ct);
            }

            await db.SaveChangesAsync(ct);

            // 5) If totals were missing, roll up from items (never overwrite valid OCR totals)
            await RecomputeHeaderIfItemsExistAsync(receiptId, ct);
        }

        #region Helpers

        private async Task RecomputeHeaderIfItemsExistAsync(Guid receiptId, CancellationToken ct)
        {
            // Keep OCR totals if present
            var current = await db.Receipts.AsNoTracking()
                .Where(r => r.Id == receiptId)
                .Select(r => new { r.SubTotal, r.Total, r.Tax })
                .FirstAsync(ct);

            if (current.SubTotal is not null || current.Total is not null)
                return;

            var hasItems = await db.ReceiptItems.AnyAsync(x => x.ReceiptId == receiptId, ct);
            if (!hasItems) return;

            // Note: by this point, system Adjustment exists only when Status==Parsed.
            var agg = await db.ReceiptItems
                .Where(x => x.ReceiptId == receiptId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Sub = g.Sum(x => x.LineSubtotal),
                    Tax = g.Sum(x => x.Tax ?? 0m),
                    Tot = g.Sum(x => x.LineTotal)
                })
                .SingleAsync(ct);

            await db.Receipts.Where(r => r.Id == receiptId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.SubTotal, _ => Round2(agg.Sub))
                    .SetProperty(r => r.Tax, _ => agg.Tax == 0m ? (decimal?)null : Round2(agg.Tax))
                    .SetProperty(r => r.Total, _ => Round2(agg.Tot))
                    .SetProperty(r => r.UpdatedAt, _ => clock.UtcNow), ct);
        }

        private static ParsedReceipt BuildParsedReceipt(Receipt r)
        {
            var items = r.Items
                .Where(i => !i.IsSystemGenerated &&
                            !string.Equals(i.Label, AutoAdjLabel, StringComparison.OrdinalIgnoreCase))
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
            // Remove any non-system "Adjustment" stragglers
            var rogueAdjustments = r.Items
                .Where(x => !x.IsSystemGenerated &&
                            string.Equals(x.Label, AutoAdjLabel, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (rogueAdjustments.Count > 0)
                db.ReceiptItems.RemoveRange(rogueAdjustments);

            var adjustment = r.Items.FirstOrDefault(x =>
                x.IsSystemGenerated &&
                string.Equals(x.Label, AutoAdjLabel, StringComparison.OrdinalIgnoreCase));

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
                    Label = AutoAdjLabel,
                    Notes = AutoAdjNote,
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
                adjustment.Notes = AutoAdjNote;
                adjustment.UpdatedAt = clock.UtcNow;
                ReceiptItemMaps.Recalculate(adjustment);
                db.ReceiptItems.Update(adjustment);
            }

            return Task.CompletedTask;
        }

        private static decimal Round2(decimal v) =>
            decimal.Round(v, 2, MidpointRounding.AwayFromZero);
    }

    #endregion
}
