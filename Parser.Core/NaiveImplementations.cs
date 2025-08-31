using System.Text.RegularExpressions;

namespace Parser.Core;

public sealed class StubOcrReader : IOcrReader
{
    public Task<OcrResult> ReadTextAsync(Uri fileUri, CancellationToken ct = default)
    {
        var text = """
        Demo Merchant
        08/31/2025 08:40
        Latte 4.95
        Croissant 3.25
        Subtotal 8.20
        Tax 0.74
        Total 8.94
        """;
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return Task.FromResult(new OcrResult(text, lines));
    }
}

public sealed class SimpleLlmNormalizer : IReceiptNormalizer
{
    public Task<NormalizedReceipt> NormalizeAsync(OcrResult ocr, CancellationToken ct = default)
    {
        decimal sub=0, tax=0, tip=0, fees=0, total=0;
        var items = new List<Item>();

        foreach (var raw in ocr.Lines)
        {
            var line = raw.Trim();
            if (line.Contains("subtotal", StringComparison.OrdinalIgnoreCase)) sub = MoneyAtEnd(line);
            else if (line.Contains("tax", StringComparison.OrdinalIgnoreCase))  tax = MoneyAtEnd(line);
            else if (line.Contains("tip", StringComparison.OrdinalIgnoreCase))  tip = MoneyAtEnd(line);
            else if (line.Contains("fee", StringComparison.OrdinalIgnoreCase))  fees = MoneyAtEnd(line);
            else if (line.Contains("total", StringComparison.OrdinalIgnoreCase)) total = MoneyAtEnd(line);
            else
            {
                var m = Regex.Match(line, @"(.+?)\s+(\d+(?:\.\d{1,2})?)$");
                if (m.Success)
                {
                    var name = m.Groups[1].Value.Trim();
                    var price = decimal.Parse(m.Groups[2].Value);
                    items.Add(new Item(name, 1, price, price));
                }
            }
        }
        if (total == 0) total = sub + tax + tip + fees;

        var merchant = new Merchant(GuessMerchant(ocr.Lines), null, null, null);
        var tx = new Transaction(GuessDate(ocr.Lines));
        var meta = new Meta("stub", 0.5, "generic");

        return Task.FromResult(new NormalizedReceipt(merchant, tx, items, new Totals(sub, tax, tip, fees, total), meta));

        static decimal MoneyAtEnd(string s)
            => decimal.TryParse(Regex.Match(s, @"(\d+(?:\.\d{1,2})?)\s*$").Groups[1].Value, out var v) ? v : 0m;
        static string? GuessMerchant(IEnumerable<string> lines)
            => lines.FirstOrDefault(l => l.Length <= 30 && Regex.IsMatch(l, @"[A-Za-z]"))?.Trim();
        static DateTimeOffset? GuessDate(IEnumerable<string> lines)
            => lines.Select(l => DateTimeOffset.TryParse(l, out var d) ? d : (DateTimeOffset?)null).FirstOrDefault(d => d.HasValue);
    }
}

public sealed class DefaultReconciler : IReceiptReconciler
{
    public NormalizedReceipt Reconcile(NormalizedReceipt input)
    {
        var sum = input.Items.Sum(i => i.LineTotal);
        var sub = input.Totals.SubTotal == 0 ? sum : input.Totals.SubTotal;
        var recomputedTotal = sub + input.Totals.Tax + input.Totals.Tip + input.Totals.Fees;
        var total = input.Totals.Total == 0 ? recomputedTotal : input.Totals.Total;
        if (Math.Abs((double)(total - recomputedTotal)) < 0.02) total = recomputedTotal;
        return input with { Totals = input.Totals with { SubTotal = sub, Total = total } };
    }
}
