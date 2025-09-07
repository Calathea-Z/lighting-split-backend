namespace Api.Abstractions.Reconciliation;

/// <summary>Server-side policy caps/toggles for auto adjustments.</summary>
public sealed class ReconcileOptions
{
    /// <summary>When true, auto-adjustments are only allowed once status == Parsed.</summary>
    public bool EnableOnlyWhenParsed { get; set; } = true;

    /// <summary>When false, a printed Subtotal must exist to allow auto-adjustments.</summary>
    public bool AllowWithoutPrintedSubtotal { get; set; } = false;

    /// <summary>Absolute cap (USD) for auto-adjust magnitude.</summary>
    public decimal MaxAbs { get; set; } = 5.00m;

    /// <summary>Percentage cap relative to BaselineSubtotal (e.g., 0.015 = 1.5%).</summary>
    public decimal MaxPct { get; set; } = 0.015m;

    /// <summary>At/under this, label may be shown as “Rounding Adjustment”.</summary>
    public decimal TinyRounding { get; set; } = 0.02m;
}
