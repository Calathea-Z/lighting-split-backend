using System.Diagnostics;
using Api.Contracts.Reconciliation;
using Api.Options;
using Api.Services.Reconciliation.Abstractions;


public sealed class RecieptReconciliationCalculator : IReceiptReconciliationCalculator
{
    private readonly ReconciliationOptions _opt;

    public RecieptReconciliationCalculator(ReconciliationOptions? opt = null)
    {
        _opt = opt ?? new ReconciliationOptions();
        Debug.Assert(_opt.Epsilon > 0);
    }

    public ReconcileResult Reconcile(ParsedReceipt receipt)
    {
        var itemsSum = Math.Round(receipt.Items.Sum(i => i.LineTotal), 2);
        var t = receipt.Totals;

        // Subtotal-first baseline
        decimal? baseline = t.Subtotal;

        // If no printed subtotal, try derive from Total − Tax − Tip (when available)
        if (baseline is null && t.Total is not null)
        {
            var tax = t.Tax ?? 0m;
            var tip = t.Tip ?? 0m;
            baseline = t.Total.Value - tax - tip;
        }

        // If still null, baseline = items sum (we’ll trust items)
        baseline ??= itemsSum;

        baseline = Math.Round(baseline.Value, 2);

        var discrepancy = Math.Round(itemsSum - baseline.Value, 2);
        var withinEps = Math.Abs(discrepancy) <= _opt.Epsilon;

        // Optional grand total consistency check (only if all parts exist)
        bool totalConsistent = true;
        if (t.Total is not null && t.Subtotal is not null)
        {
            var composed = (t.Subtotal ?? 0m) + (t.Tax ?? 0m) + (t.Tip ?? 0m);
            totalConsistent = Math.Abs(Math.Round(composed - t.Total.Value, 2)) <= _opt.Epsilon;
        }

        var status = (withinEps && totalConsistent) ? ParseStatus.Parsed : ParseStatus.ParsedNeedsReview;

        return new ReconcileResult(
            Status: status,
            ItemsSum: itemsSum,
            BaselineSubtotal: baseline.Value,
            Discrepancy: discrepancy,
            NeedsAdjustment: !withinEps, // only true when discrepancy exceeds ε
            Reason: withinEps
                ? (totalConsistent ? null : "Grand total mismatch outside ε.")
                : $"Items vs baseline subtotal differ by {discrepancy:0.00}."
        );
    }
}