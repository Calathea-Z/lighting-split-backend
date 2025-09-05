using System.Text.Json.Serialization;

namespace Api.Dtos.Receipts.Responses.Items;

// Item detail shown in UI / API
public sealed record ReceiptItemDto(
    Guid Id,
    Guid ReceiptId,
    string Label,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Unit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Notes,
    int Position,
    decimal Qty,
    decimal UnitPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Discount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Tax,
    decimal LineSubtotal,
    decimal LineTotal,
    uint Version,
    bool IsSystemGenerated
);
