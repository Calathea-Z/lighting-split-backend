namespace Api.Dtos;

public record ReceiptItemDto(
    Guid Id,
    string Label,
    int Qty,
    decimal UnitPrice
);