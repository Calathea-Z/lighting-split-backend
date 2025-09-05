using Api.Contracts.Reconciliation;

namespace Api.Services.Reconciliation.Abstractions;

public interface IReceiptReconciliationCalculator
{
    ReconcileResult Reconcile(ParsedReceipt receipt);
}
