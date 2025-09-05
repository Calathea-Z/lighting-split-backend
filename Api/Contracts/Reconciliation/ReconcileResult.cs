using Api.Abstractions.Receipts;

namespace Api.Contracts.Reconciliation
{
    public sealed record ReconcileResult(
        ParseStatus Status,
        decimal ItemsSum,
        decimal BaselineSubtotal,
        decimal Discrepancy,
        bool NeedsAdjustment,
        string? Reason,
        BaselineSource Source = BaselineSource.Subtotal);
}
