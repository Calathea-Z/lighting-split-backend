namespace Api.Abstractions.Reconciliation;

public sealed record ParsedMoneyTotals(decimal? Subtotal, decimal? Tax, decimal? Tip, decimal? Total);
