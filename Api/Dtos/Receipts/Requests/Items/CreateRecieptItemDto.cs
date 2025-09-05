using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests.Items;

public sealed class CreateReceiptItemDto : IValidatableObject
{
    [Required, MaxLength(200)]
    public string Label { get; set; } = "";

    [MaxLength(16)]
    public string? Unit { get; set; }

    [MaxLength(64)]
    public string? Category { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [Range(0, int.MaxValue)]
    public int Position { get; set; } = 0;

    [Range(0, 1_000_000_000)] // allow 0 to support free items/headers
    public decimal Qty { get; set; } = 1m;

    [Range(0, 1_000_000_000)]
    public decimal UnitPrice { get; set; } = 0m;

    [Range(0, 1_000_000_000)]
    public decimal? Discount { get; set; }

    [Range(0, 1_000_000_000)]
    public decimal? Tax { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        Label = Label?.Trim() ?? "";
        Unit = string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim();
        Category = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim();
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        if (string.IsNullOrWhiteSpace(Label))
            yield return VR("Label is required.", nameof(Label));

        if (!HasMaxScale(Qty, 3)) yield return VR("Qty supports up to 3 decimal places.", nameof(Qty));
        if (!HasMaxScale(UnitPrice, 2)) yield return VR("UnitPrice supports up to 2 decimal places.", nameof(UnitPrice));
        if (Discount is { } d && !HasMaxScale(d, 2)) yield return VR("Discount supports up to 2 decimal places.", nameof(Discount));
        if (Tax is { } t && !HasMaxScale(t, 2)) yield return VR("Tax supports up to 2 decimal places.", nameof(Tax));

        var lineSubtotal = Qty * UnitPrice;
        if (Discount is { } disc && disc > lineSubtotal + 0.01m)
            yield return VR("Discount cannot exceed line subtotal.", nameof(Discount));
    }

    private static bool HasMaxScale(decimal value, int maxScale)
    {
        value = Math.Abs(value);
        var scale = BitConverter.GetBytes(decimal.GetBits(value)[3])[2]; // 0..28
        return scale <= maxScale;
    }
    private static ValidationResult VR(string msg, params string[] members)
        => new(msg, members);
}
