using System.ComponentModel.DataAnnotations;
using Api.Dtos.Receipts.Requests.Items;

namespace Api.Dtos.Receipts.Requests;

public sealed class CreateReceiptDto : IValidatableObject
{
    // Require at least one item
    [Required, MinLength(1)]
    public List<CreateReceiptItemDto> Items { get; set; } = new();

    // Header totals (optional) — must be >= 0 when provided
    [Range(0, 999_999_999.99)] public decimal? SubTotal { get; set; }
    [Range(0, 999_999_999.99)] public decimal? Tax { get; set; }
    [Range(0, 999_999_999.99)] public decimal? Tip { get; set; }
    [Range(0, 999_999_999.99)] public decimal? Total { get; set; }

    // Optional metadata
    [MaxLength(100_000)] public string? RawText { get; set; }
    [MaxLength(200)] public string? StoreName { get; set; }
    public DateTimeOffset? PurchasedAt { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        // normalize strings
        RawText = string.IsNullOrWhiteSpace(RawText) ? null : RawText.Trim();
        StoreName = string.IsNullOrWhiteSpace(StoreName) ? null : StoreName.Trim();
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        // money scale guards (? 2 decimals)
        if (SubTotal is { } s && !HasMaxScale(s, 2)) yield return VR("SubTotal supports up to 2 decimal places.", nameof(SubTotal));
        if (Tax is { } t && !HasMaxScale(t, 2)) yield return VR("Tax supports up to 2 decimal places.", nameof(Tax));
        if (Tip is { } p && !HasMaxScale(p, 2)) yield return VR("Tip supports up to 2 decimal places.", nameof(Tip));
        if (Total is { } o && !HasMaxScale(o, 2)) yield return VR("Total supports up to 2 decimal places.", nameof(Total));

        // Total consistency check (only if all 4 provided)
        if (Total.HasValue && SubTotal.HasValue && Tax.HasValue && Tip.HasValue)
        {
            var computed = SubTotal.Value + Tax.Value + Tip.Value;
            if (Math.Abs(computed - Total.Value) > 0.01m)
                yield return VR("Total must equal SubTotal + Tax + Tip (±$0.01).",
                    nameof(Total), nameof(SubTotal), nameof(Tax), nameof(Tip));
        }

        // PurchasedAt: not absurdly in the future (> 7 days)
        if (PurchasedAt is { } ts && ts > DateTimeOffset.UtcNow.AddDays(7))
            yield return VR("PurchasedAt cannot be more than 7 days in the future.", nameof(PurchasedAt));

        // Ensure items present and valid-looking
        if (Items is null || Items.Count == 0)
            yield return VR("At least one item is required.", nameof(Items));
    }

    private static bool HasMaxScale(decimal value, int maxScale)
    {
        value = Math.Abs(value);
        var scale = BitConverter.GetBytes(decimal.GetBits(value)[3])[2]; // 0..28
        return scale <= maxScale;
    }

    private static ValidationResult VR(string msg, params string[] members) => new(msg, members);
}
