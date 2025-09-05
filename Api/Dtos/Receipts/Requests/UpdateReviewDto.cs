using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed record UpdateReviewDto(
    bool NeedsReview,

    [property: MaxLength(2000)]
    string? Reason
);
