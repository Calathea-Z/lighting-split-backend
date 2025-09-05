using Api.Abstractions.Receipts;
using Api.Dtos.Receipts.Responses.Items;
using System.Text.Json.Serialization;

namespace Api.Dtos.Receipts.Responses;

public sealed record ReceiptDetailDto(
    Guid Id,

    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    ReceiptStatus Status,

    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RawText,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? SubTotal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Tax,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Tip,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Total,

    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,

    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? ComputedItemsSubtotal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? BaselineSubtotal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Discrepancy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,

    bool NeedsReview,

    IReadOnlyList<ReceiptItemDto> Items
);
