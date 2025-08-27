namespace Api.Dtos;

public record CreateReceiptItemDto(
    string Label,
    int Qty,
    decimal UnitPrice
);