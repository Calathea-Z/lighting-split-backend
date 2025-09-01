namespace Api.Dtos;

// High-level, lightweight view (used by lists and quick UI refreshes)
public sealed record ReceiptSummaryDto(
    Guid Id,
    string Status,
    decimal? SubTotal,
    decimal? Tax,
    decimal? Tip,
    decimal? Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ItemCount
);