namespace Api.Abstractions.Transport;

public sealed record UpdateReceiptItemRequest(
    string Label,
    decimal Qty,
    decimal UnitPrice,
    string? Unit = null,
    string? Category = null,
    string? Notes = null,
    int? Position = null,
    uint? Version = null
);