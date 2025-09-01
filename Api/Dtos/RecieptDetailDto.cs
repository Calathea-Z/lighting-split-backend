namespace Api.Dtos;

// Full detail (used by detail screens / edit views)
public sealed record ReceiptDetailDto(
    Guid Id,
    string? OwnerUserId,
    string Status,
    string? ParseError,
    string OriginalFileUrl,
    string BlobContainer,
    string BlobName,
    string? RawText,
    decimal? SubTotal,
    decimal? Tax,
    decimal? Tip,
    decimal? Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ReceiptItemDto> Items
);