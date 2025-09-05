using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed record UpdateReviewDto(
    bool NeedsReview,
    [property: MaxLength(2000)] string? Reason
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        if (!NeedsReview && !string.IsNullOrWhiteSpace(Reason))
            yield return new ValidationResult(
                "Reason should only be provided when NeedsReview = true.",
                new[] { nameof(Reason) });
    }
}
