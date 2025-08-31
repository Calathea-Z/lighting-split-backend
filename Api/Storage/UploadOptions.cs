namespace Api.Storage;

public sealed class UploadOptions
{
    // e.g. "uploads" (relative to ContentRoot) for dev
    public string RootFolder { get; set; } = "uploads";
    // Public base path for serving files (via UseStaticFiles); e.g. "/uploads"
    public string PublicRequestPath { get; set; } = "/uploads";
    // Max 10 MB default
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;
    // Allow images + pdf for now
    public string[] AllowedContentTypes { get; set; } = new[]
    {
        "image/jpeg","image/png","image/webp","image/heic","image/heif","application/pdf"
    };
}