using System.ComponentModel.DataAnnotations;

namespace Api.Options;

public sealed class UploadOptions
{
    [Required, MinLength(1)]
    public string RootFolder { get; set; } = "uploads";

    // Must start with "/" (e.g., "/uploads")
    [Required, RegularExpression("^/[A-Za-z0-9._~/-]*$",
        ErrorMessage = "PublicRequestPath must start with '/' and contain URL-safe characters.")]
    public string PublicRequestPath { get; set; } = "/uploads";

    // 1 KB – 500 MB
    [Range(1024, 524_288_000)]
    public int MaxBytes { get; set; } = 10 * 1024 * 1024;

    // MIME whitelist
    [MinLength(1)]
    public string[] AllowedContentTypes { get; set; } =
        new[] { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    // Optional: extension whitelist to pair with MIME check
    public string[] AllowedExtensions { get; set; } =
        new[] { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };
}
