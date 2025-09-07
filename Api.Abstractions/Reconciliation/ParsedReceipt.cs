using System.Collections.Generic;

namespace Api.Abstractions.Reconciliation;

public sealed record ParsedReceipt(IReadOnlyList<ParsedItem> Items, ParsedMoneyTotals Totals, string RawText);
