namespace Api.Services.Receipts.Abstractions
{
    public interface IReceiptReconciliationOrchestrator
    {
        Task ReconcileAsync(Guid receiptId, CancellationToken ct = default);
    }
}
