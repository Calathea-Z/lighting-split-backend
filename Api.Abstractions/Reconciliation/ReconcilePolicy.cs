using Api.Abstractions.Math;
using Api.Abstractions.Receipts;

namespace Api.Abstractions.Reconciliation;

/// <summary>Lightweight policy helper used by API (and optionally Functions) to decide if an auto adjustment is allowed.</summary>
public static class ReconcilePolicy
{
    /// <param name="status">Receipt status.</param>
    /// <param name="hasPrintedSubtotal">True if a printed Subtotal was parsed/present.</param>
    /// <param name="baseline">Baseline subtotal (usually printed Subtotal, else computed).</param>
    /// <param name="delta">Baseline - ItemsSum (positive means we need a negative adjustment).</param>
    /// <returns>(allow, reason) where reason ∈ { "ok","status","no_subtotal","abs_cap","pct_cap" }.</returns>
    public static (bool allow, string reason) CanAutoAdjust(
        ReceiptStatus status,
        bool hasPrintedSubtotal,
        decimal baseline,
        decimal delta,
        ReconcileOptions opts)
    {
        if (opts.EnableOnlyWhenParsed && status != ReceiptStatus.Parsed) return (false, "status");
        if (!opts.AllowWithoutPrintedSubtotal && !hasPrintedSubtotal)     return (false, "no_subtotal");

        var absOk = System.Math.Abs(delta) <= opts.MaxAbs;

        // If baseline <= 0, percentage cap is non-informative → rely on absolute only
        var pctCap = baseline <= 0 ? decimal.MaxValue : Money.Round2(baseline * opts.MaxPct);
        var pctOk  = System.Math.Abs(delta) <= pctCap;

        if (!absOk) return (false, "abs_cap");
        if (!pctOk) return (false, "pct_cap");
        return (true, "ok");
    }
}
