namespace Api.Contracts.Reconciliation
{
    public sealed record ParsedItem(string Description, int Qty, decimal UnitPrice)
    {
        public decimal LineTotal => decimal.Round(Qty * UnitPrice, 2, MidpointRounding.AwayFromZero);
    }
}
