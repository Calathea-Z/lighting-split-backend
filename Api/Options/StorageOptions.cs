using System.ComponentModel.DataAnnotations;

namespace Api.Options;

public sealed class StorageOptions
{
    [Required]
    [RegularExpression("^(?:[a-z0-9](?:[a-z0-9-]{1,61}[a-z0-9])?)$",
        ErrorMessage = "ReceiptsContainer must be 3–63 chars, lowercase, numbers, and dashes.")]
    public string ReceiptsContainer { get; set; } = "receipts";

    public bool OverwriteOnUpload { get; set; } = true;
}
