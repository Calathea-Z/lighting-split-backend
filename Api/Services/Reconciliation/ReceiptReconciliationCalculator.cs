using System.Diagnostics;
using Api.Abstractions.Receipts;
using Api.Contracts.Reconciliation;
using Api.Options;
using Api.Services.Reconciliation.Abstractions;

public sealed class ReceiptReconciliationCalculator : IReceiptReconciliationCalculator
{
    private readonly ReconciliationOptions _opt;

    public ReceiptReconciliationCalculator(ReconciliationOptions? opt = null)
    {
        _opt = opt ?? new ReconciliationOptions();
        Debug.Assert(_opt.Epsilon > 0);
    }

    public ReconcileResult Reconcile(ParsedReceipt receipt)
    {
        var itemsSum = Math.Round(receipt.Items.Sum(i => i.LineTotal), 2);
        var t = receipt.Totals;

        // Choose baseline + record source
        decimal? baseline = t.Subtotal;
        var source = BaselineSource.Subtotal;

        if (baseline is null && t.Total is not null)
        {
            var tax = t.Tax ?? 0m;
            var tip = t.Tip ?? 0m;
            baseline = t.Total.Value - tax - tip;
            source = BaselineSource.Total;
        }

        if (baseline is null)
        {
            baseline = itemsSum;
            source = BaselineSource.Items;
        }

        baseline = Math.Round(baseline.Value, 2);

        var discrepancy = Math.Round(itemsSum - baseline.Value, 2);
        var withinEps = Math.Abs(discrepancy) <= _opt.Epsilon;

        // Optional grand total consistency check (only if parts exist)
        var totalConsistent = true;
        if (t.Total is not null && t.Subtotal is not null)
        {
            var composed = (t.Subtotal ?? 0m) + (t.Tax ?? 0m) + (t.Tip ?? 0m);
            totalConsistent = Math.Abs(Math.Round(composed - t.Total.Value, 2)) <= _opt.Epsilon;
        }

        // Map to your ParseStatus enum (Success / Partial / Failed)
        var status = (withinEps && totalConsistent) ? ParseStatus.Success : ParseStatus.Partial;

        return new ReconcileResult(
            Status: status,
            ItemsSum: itemsSum,
            BaselineSubtotal: baseline.Value,
            Discrepancy: discrepancy,
            NeedsAdjustment: !withinEps,
            Reason: withinEps
                ? (totalConsistent ? null : "Grand total mismatch outside ε.")
                : $"Items vs baseline subtotal differ by {discrepancy:0.00}.",
            Source: source
        );
    }
}
