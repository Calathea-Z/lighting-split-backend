using System.Text.Json.Serialization;

namespace Api.Dtos.Receipts.Responses;

// High-level, lightweight view (used by lists and quick UI refreshes)
public sealed record ReceiptSummaryDto(
    Guid Id,
    string Status,

    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? SubTotal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Tax,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Tip,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Total,

    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ItemCount,

    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? ComputedItemsSubtotal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? BaselineSubtotal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Discrepancy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Reason,

    bool NeedsReview
);
