namespace Api.Abstractions.Transport;

public sealed record CreateReceiptItemRequest(
    string Label,
    decimal Qty,
    decimal UnitPrice,
    string? Unit = null,
    string? Category = null,
    string? Notes = null,
    decimal? Discount = null,
    decimal? Tax = null,
    int? Position = null
);
