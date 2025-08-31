using Microsoft.Extensions.Options;

namespace Api.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly UploadOptions _opts;

    public LocalFileStorage(IWebHostEnvironment env, IOptions<UploadOptions> opts)
    {
        _env = env;
        _opts = opts.Value;
    }

    public async Task<StoredFile> SaveAsync(IFormFile file, string? subfolder = null, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) throw new InvalidOperationException("No file provided.");
        if (file.Length > _opts.MaxBytes) throw new InvalidOperationException("File too large.");
        if (!_opts.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported content type: {file.ContentType}");

        var ext = Path.GetExtension(file.FileName);
        var name = $"{Guid.NewGuid():N}{ext}";
        var rel = string.IsNullOrWhiteSpace(subfolder)
            ? Path.Combine(_opts.RootFolder, name)
            : Path.Combine(_opts.RootFolder, subfolder, name);

        var abs = Path.Combine(_env.ContentRootPath, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);

        await using (var fs = new FileStream(abs, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await file.CopyToAsync(fs, ct);
        }

        // URL is served by UseStaticFiles mapped to PublicRequestPath
        var reqPath = _opts.PublicRequestPath.TrimEnd('/');
        var relForUrl = rel.Replace('\\', '/'); // normalize
        var url = $"{reqPath}/{Path.GetFileName(relForUrl)}";
        return new StoredFile(url, rel, name, file.Length, file.ContentType);
    }
}