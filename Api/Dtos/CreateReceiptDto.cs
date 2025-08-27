namespace Api.Dtos;

public record CreateReceiptDto(
    string? OwnerUserId,
    string? OriginalFileUrl,
    string? RawText,
    List<CreateReceiptItemDto> Items
);