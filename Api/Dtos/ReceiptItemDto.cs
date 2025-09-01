namespace Api.Dtos;

// Item detail shown in UI
public sealed record ReceiptItemDto(
    Guid Id,
    string Label,
    string? Unit,
    string? Category,
    string? Notes,
    int Position,
    decimal Qty,
    decimal UnitPrice,
    decimal? Discount,
    decimal? Tax,
    decimal LineSubtotal,
    decimal LineTotal,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    uint Version            // <- maps to xmin for concurrency on updates
);