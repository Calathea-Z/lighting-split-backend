using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Requests.Items;

public sealed class UpdateReceiptItemDto : IValidatableObject
{
    /// <summary>Concurrency token (xmin).</summary>
    [Required] public uint Version { get; set; }

    [MaxLength(200)] public string? Label { get; set; }
    [MaxLength(16)] public string? Unit { get; set; }
    [MaxLength(64)] public string? Category { get; set; }
    [MaxLength(1000)] public string? Notes { get; set; }

    [Range(0, int.MaxValue)] public int? Position { get; set; }
    [Range(0, 1_000_000_000)] public decimal? Qty { get; set; }
    [Range(0, 1_000_000_000)] public decimal? UnitPrice { get; set; }
    [Range(0, 1_000_000_000)] public decimal? Discount { get; set; }
    [Range(0, 1_000_000_000)] public decimal? Tax { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        if (Version == 0) yield return VR("Version must be > 0.", nameof(Version));

        // normalize strings
        Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim();
        Unit = string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim();
        Category = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim();
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        // at least one field must be provided
        var any =
            Label is not null || Unit is not null || Category is not null || Notes is not null ||
            Position.HasValue || Qty.HasValue || UnitPrice.HasValue || Discount.HasValue || Tax.HasValue;

        if (!any) yield return VR("Provide at least one field to update.");

        if (Qty.HasValue && !HasMaxScale(Qty.Value, 3)) yield return VR("Qty supports up to 3 decimal places.", nameof(Qty));
        if (UnitPrice.HasValue && !HasMaxScale(UnitPrice.Value, 2)) yield return VR("UnitPrice supports up to 2 decimal places.", nameof(UnitPrice));
        if (Discount.HasValue && !HasMaxScale(Discount.Value, 2)) yield return VR("Discount supports up to 2 decimal places.", nameof(Discount));
        if (Tax.HasValue && !HasMaxScale(Tax.Value, 2)) yield return VR("Tax supports up to 2 decimal places.", nameof(Tax));

        if (Qty.HasValue && UnitPrice.HasValue && Discount.HasValue)
        {
            var subtotal = Qty.Value * UnitPrice.Value;
            if (Discount.Value > subtotal + 0.01m)
                yield return VR("Discount cannot exceed line subtotal.", nameof(Discount));
        }
    }

    private static bool HasMaxScale(decimal value, int maxScale)
    {
        value = Math.Abs(value);
        var scale = BitConverter.GetBytes(decimal.GetBits(value)[3])[2];
        return scale <= maxScale;
    }

    private static ValidationResult VR(string msg, params string[] members) => new(msg, members);
}
