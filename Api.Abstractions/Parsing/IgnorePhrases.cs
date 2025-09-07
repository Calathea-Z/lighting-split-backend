namespace Api.Abstractions.Parsing;

/// <summary>Vocabulary of phrases that indicate totals/promos/meta (not line items).</summary>
public static class IgnorePhrases
{
    public static readonly string[] Values =
    [
        "pre-discount subtotal",
        "discount total",
        "spend",
        "save",
        "promo",
        "promotion",
        "coupon",
        "member",
        "loyalty",
        "rewards",
        "bogo",
        "% off",
        "tax",
        "sales tax",
        "tip",
        "gratuity",
        "service",
        "total",
        "amount due"
    ];
}
