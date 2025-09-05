using Api.Options;
using Microsoft.Extensions.Options;

namespace Api.Storage;

/// <summary>
/// Stores uploaded files on the local filesystem under a configured root folder,
/// applying validation rules from <see cref="UploadOptions"/> (size, MIME type, extension).
/// Files are served back through a static file middleware mapped to <c>PublicRequestPath</c>.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private static readonly StringComparison Ci = StringComparison.OrdinalIgnoreCase;

    private readonly IWebHostEnvironment _env;
    private readonly UploadOptions _opts;

    /// <summary>
    /// Creates a new local file storage service.
    /// </summary>
    /// <param name="env">Hosting environment used to resolve the content root path.</param>
    /// <param name="opts">Upload configuration (limits, paths, allowed types).</param>
    public LocalFileStorage(IWebHostEnvironment env, IOptions<UploadOptions> opts)
    {
        _env = env;
        _opts = opts.Value;
    }

    /// <summary>
    /// Saves an uploaded file to disk under the configured root folder.
    /// Validates size, MIME type, and extension before writing.
    /// Returns metadata describing where the file was stored and how to access it.
    /// </summary>
    /// <param name="file">The incoming file to persist.</param>
    /// <param name="subfolder">Optional logical subfolder (sanitized to prevent traversal).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="StoredFile"/> with URL, relative path, size, and content type.</returns>
    /// <exception cref="InvalidOperationException">Thrown if file is missing, too large, or of disallowed type.</exception>
    public async Task<StoredFile> SaveAsync(IFormFile file, string? subfolder = null, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
            throw new InvalidOperationException("No file provided.");
        if (file.Length > _opts.MaxBytes)
            throw new InvalidOperationException("File too large.");

        // Validate MIME type against whitelist
        var contentTypeOk = _opts.AllowedContentTypes?.Any() == true &&
                            _opts.AllowedContentTypes.Any(ct => string.Equals(ct, file.ContentType, Ci));

        // Validate extension against whitelist
        var ext = Path.GetExtension(file.FileName);
        var extOk = _opts.AllowedExtensions?.Any() != true ||  // if none configured, skip
                    _opts.AllowedExtensions.Any(e => string.Equals(e, ext, Ci));

        if (!contentTypeOk || !extOk)
            throw new InvalidOperationException($"Unsupported file type: {file.ContentType} {ext}");

        // Clean subfolder to remove unsafe characters (prevents path traversal)
        var safeSub = SanitizeSubfolder(subfolder);

        // Generate unique filename (GUID + original extension)
        var name = $"{Guid.NewGuid():N}{ext}";
        var relative = string.IsNullOrWhiteSpace(safeSub)
            ? Path.Combine(_opts.RootFolder, name)
            : Path.Combine(_opts.RootFolder, safeSub, name);

        // Compute absolute path within content root
        var absolute = Path.Combine(_env.ContentRootPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        // Write file to disk asynchronously
        await using var fs = new FileStream(
            absolute, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await file.CopyToAsync(fs, ct);

        // Build public URL served via StaticFiles middleware
        var reqBase = _opts.PublicRequestPath.TrimEnd('/');
        var relForUrl = relative.Replace('\\', '/'); // normalize for URL
        var url = $"{reqBase}/{relForUrl.Substring(_opts.RootFolder.Length).TrimStart('/')}";

        return new StoredFile(
            url,
            relative,
            name,
            file.Length,
            file.ContentType ?? "application/octet-stream");
    }

    /// <summary>
    /// Normalizes a subfolder path to allow only safe characters
    /// (letters, numbers, dash, underscore, dot) and strips anything unsafe.
    /// Prevents directory traversal and injection attacks.
    /// </summary>
    private static string SanitizeSubfolder(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var parts = input
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(seg => new string(seg.Where(c =>
                char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray()))
            .Where(seg => !string.IsNullOrWhiteSpace(seg));

        return string.Join(Path.DirectorySeparatorChar, parts);
    }
}
