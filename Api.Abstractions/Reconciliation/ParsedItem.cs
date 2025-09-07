using Api.Abstractions.Math;

namespace Api.Abstractions.Reconciliation;

public sealed record ParsedItem(string Description, int Qty, decimal UnitPrice)
{
    public decimal LineTotal => Money.Round2(Qty * UnitPrice);
}