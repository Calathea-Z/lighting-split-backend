namespace Api.Dtos;

public record ReceiptDetailDto(
    Guid Id,
    string? OwnerUserId,
    string OriginalFileUrl,
    string RawText,
    DateTime CreatedAt,
    decimal? SubTotal,
    decimal? Tax,
    decimal? Tip,
    decimal? Total,
    List<ReceiptItemDto> Items
);