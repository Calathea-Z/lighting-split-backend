using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed record UpdateTotalsDto(
    [property: Range(0, 999_999_999.99)] decimal? SubTotal,
    [property: Range(0, 999_999_999.99)] decimal? Tax,
    [property: Range(0, 999_999_999.99)] decimal? Tip,
    [property: Range(0, 999_999_999.99)] decimal? Total
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        if (Total.HasValue && SubTotal.HasValue && Tax.HasValue && Tip.HasValue)
        {
            var computed = SubTotal.Value + Tax.Value + Tip.Value;
            if (Math.Abs(computed - Total.Value) > 0.01m)
            {
                yield return new ValidationResult(
                    "Total must equal SubTotal + Tax + Tip (±$0.01).",
                    new[] { nameof(Total), nameof(SubTotal), nameof(Tax), nameof(Tip) }
                );
            }
        }
    }
}
