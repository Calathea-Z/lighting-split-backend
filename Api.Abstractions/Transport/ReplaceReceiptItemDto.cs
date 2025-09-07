namespace Api.Abstractions.Transport;

/// <summary>Line item used by replace-mode API. Prices are pre-tax unless your model defines otherwise.</summary>
public sealed record ReplaceReceiptItemDto(
    string Label,
    decimal Qty,
    decimal UnitPrice,
    string? Unit = null,
    string? Category = null,
    string? Notes = null
);
