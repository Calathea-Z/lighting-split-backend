namespace Api.Dtos;

public record ReceiptSummaryDto(
    Guid Id,
    string? OwnerUserId,
    string OriginalFileUrl,
    DateTime CreatedAt,
    decimal? SubTotal,
    decimal? Tax,
    decimal? Tip,
    decimal? Total,
    int ItemCount
);