using Api.Dtos.Receipts.Requests.Items;
using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed class CreateReceiptDto : IValidatableObject
{
    // Require at least one item
    [Required, MinLength(1)]
    public List<CreateReceiptItemDto> Items { get; set; } = [];

    // Header totals (optional) — must be >= 0 when provided
    [Range(0, 999_999_999.99)]
    public decimal? SubTotal { get; set; }

    [Range(0, 999_999_999.99)]
    public decimal? Tax { get; set; }

    [Range(0, 999_999_999.99)]
    public decimal? Tip { get; set; }

    [Range(0, 999_999_999.99)]
    public decimal? Total { get; set; }

    // Optional metadata
    [MaxLength(100_000)]
    public string? RawText { get; set; }

    [MaxLength(200)]
    public string? StoreName { get; set; }

    public DateTimeOffset? PurchasedAt { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    // Cross-field validations
    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        // If any header piece is provided AND all three parts exist,
        // check that Total ? SubTotal + Tax + Tip (within 1 cent).
        if (Total.HasValue && SubTotal.HasValue && Tax.HasValue && Tip.HasValue)
        {
            var computed = SubTotal.Value + Tax.Value + Tip.Value;
            if (Math.Abs(computed - Total.Value) > 0.01m)
            {
                yield return new ValidationResult(
                    "Total must equal SubTotal + Tax + Tip (±$0.01).",
                    new[] { nameof(Total), nameof(SubTotal), nameof(Tax), nameof(Tip) });
            }
        }

        // Guard: PurchasedAt should not be far future ( > 7 days ahead )
        if (PurchasedAt is { } ts && ts > DateTimeOffset.UtcNow.AddDays(7))
        {
            yield return new ValidationResult(
                "PurchasedAt cannot be more than 7 days in the future.",
                new[] { nameof(PurchasedAt) });
        }
    }
}
