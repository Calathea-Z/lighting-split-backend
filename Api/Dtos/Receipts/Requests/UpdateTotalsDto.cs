using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests;

public sealed record UpdateTotalsDto(
    [param: Range(0, 999_999_999.99)] decimal? SubTotal,
    [param: Range(0, 999_999_999.99)] decimal? Tax,
    [param: Range(0, 999_999_999.99)] decimal? Tip,
    [param: Range(0, 999_999_999.99)] decimal? Total
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        // Money scale guards (? up to 2 decimals)
        if (SubTotal is { } s && !HasMaxScale(s, 2)) yield return VR("SubTotal supports up to 2 decimal places.", nameof(SubTotal));
        if (Tax is { } t && !HasMaxScale(t, 2)) yield return VR("Tax supports up to 2 decimal places.", nameof(Tax));
        if (Tip is { } p && !HasMaxScale(p, 2)) yield return VR("Tip supports up to 2 decimal places.", nameof(Tip));
        if (Total is { } o && !HasMaxScale(o, 2)) yield return VR("Total supports up to 2 decimal places.", nameof(Total));

        // Cross-field consistency check
        if (Total.HasValue && SubTotal.HasValue && Tax.HasValue && Tip.HasValue)
        {
            var computed = SubTotal.Value + Tax.Value + Tip.Value;
            if (Math.Abs(computed - Total.Value) > 0.01m)
                yield return VR("Total must equal SubTotal + Tax + Tip (±$0.01).",
                    nameof(Total), nameof(SubTotal), nameof(Tax), nameof(Tip));
        }
    }

    private static bool HasMaxScale(decimal value, int maxScale)
    {
        value = Math.Abs(value);
        var scale = BitConverter.GetBytes(decimal.GetBits(value)[3])[2]; // 0..28
        return scale <= maxScale;
    }

    private static ValidationResult VR(string msg, params string[] members) => new(msg, members);
}
