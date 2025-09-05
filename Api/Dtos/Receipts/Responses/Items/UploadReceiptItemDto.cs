using System.ComponentModel.DataAnnotations;

namespace Api.Dtos.Receipts.Responses.Items;

public sealed class UploadReceiptItemDto : IValidatableObject
{
    [Required, DataType(DataType.Upload)]
    public required IFormFile File { get; init; }

    [MaxLength(200)]
    public string? StoreName { get; init; }

    public DateTimeOffset? PurchasedAt { get; init; }

    [MaxLength(2000)]
    public string? Notes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        if (File is null || File.Length == 0)
        {
            yield return new ValidationResult("A non-empty file is required.", new[] { nameof(File) });
            yield break;
        }

        // Guard size (e.g. 10 MB)
        if (File.Length > 10 * 1024 * 1024)
            yield return new ValidationResult("File size cannot exceed 10 MB.", new[] { nameof(File) });

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp", "application/pdf" };
        var allowedExts = new[] { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };

        var contentTypeOk = allowedTypes.Contains(File.ContentType, StringComparer.OrdinalIgnoreCase);
        var ext = Path.GetExtension(File.FileName);
        var extOk = allowedExts.Contains(ext, StringComparer.OrdinalIgnoreCase);

        if (!contentTypeOk || !extOk)
            yield return new ValidationResult(
                $"Unsupported file type: {File.ContentType} {ext}.",
                new[] { nameof(File) });

        if (PurchasedAt is { } ts && ts > DateTimeOffset.UtcNow.AddDays(7))
            yield return new ValidationResult(
                "PurchasedAt cannot be more than 7 days in the future.",
                new[] { nameof(PurchasedAt) });
    }
}
