namespace Api.Contracts.Reconciliation
{
    public sealed record ParsedReceipt(IReadOnlyList<ParsedItem> Items, ParsedMoneyTotals Totals, string RawText);
}
