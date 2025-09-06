using Api.Abstractions.Receipts;
using Api.Contracts.Reconciliation;
using Api.Services.Reconciliation.Abstractions;

namespace Api.Services.Reconciliation
{
    public sealed class ReceiptReconciliationCalculator : IReceiptReconciliationCalculator
    {
        private const decimal EPS = 0.02m;

        public ReconcileResult Reconcile(ParsedReceipt parsed)
        {
            // 1) Always compute items sum from items only (no TotalPrice property needed)
            var itemsSum = Round2(parsed.Items.Sum(i => i.UnitPrice * i.Qty));

            // 2) Totals from header (may be null)
            var s = parsed.Totals.Subtotal;
            var tax = parsed.Totals.Tax ?? 0m;
            var tip = parsed.Totals.Tip ?? 0m;
            var t = parsed.Totals.Total;

            // 3) If printed totals balance, trust Subtotal as the baseline
            var totalsBalance =
                s.HasValue && t.HasValue &&
                Math.Abs((s.Value + tax + tip) - t.Value) <= EPS;

            if (totalsBalance)
            {
                var discrepancy = Round2(itemsSum - s!.Value);
                var needsReview = Math.Abs(discrepancy) > EPS;

                return new ReconcileResult(
                    Status: needsReview ? ParseStatus.Partial : ParseStatus.Success,
                    ItemsSum: itemsSum,
                    BaselineSubtotal: s.Value,
                    Discrepancy: discrepancy,
                    NeedsAdjustment: needsReview,
                    Reason: needsReview ? "Items do not add to provided subtotal." : null,
                    Source: BaselineSource.Subtotal
                );
            }

            // 4) Fallback when totals don't balance
            var baseline = s ?? t ?? 0m;
            var fallbackDiscrepancy = Round2(itemsSum - baseline);

            return new ReconcileResult(
                Status: ParseStatus.Failed,
                ItemsSum: itemsSum,
                BaselineSubtotal: baseline,
                Discrepancy: fallbackDiscrepancy,
                NeedsAdjustment: true,
                Reason: "Grand total mismatch outside ε.",
                Source: s.HasValue ? BaselineSource.Subtotal : BaselineSource.Total
            );
        }

        private static decimal Round2(decimal d) =>
            Math.Round(d, 2, MidpointRounding.AwayFromZero);
    }
}
