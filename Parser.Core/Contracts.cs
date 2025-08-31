namespace Parser.Core;

public interface IOcrReader { Task<OcrResult> ReadTextAsync(Uri fileUri, CancellationToken ct = default); }
public interface IReceiptNormalizer { Task<NormalizedReceipt> NormalizeAsync(OcrResult ocr, CancellationToken ct = default); }
public interface IReceiptReconciler { NormalizedReceipt Reconcile(NormalizedReceipt input); }

public sealed record OcrResult(string RawText, IReadOnlyList<string> Lines);

public sealed record NormalizedReceipt(
    Merchant Merchant, Transaction Transaction, List<Item> Items, Totals Totals, Meta Meta);
public sealed record Merchant(string? Name, string? Address, string? Phone, string? Category);
public sealed record Transaction(DateTimeOffset? PurchasedAt, string Currency = "USD", string? Terminal = null);
public sealed record Item(string Name, int Qty, decimal UnitPrice, decimal LineTotal, List<string>? Modifiers = null);
public sealed record Totals(decimal SubTotal, decimal Tax, decimal Tip, decimal Fees, decimal Total);
public sealed record Meta(string OcrSource, double Confidence, string VendorKey);
