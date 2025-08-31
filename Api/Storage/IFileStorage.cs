namespace Api.Storage;

public sealed record StoredFile(string Url, string RelativePath, string FileName, long Size, string ContentType);

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(IFormFile file, string? subfolder = null, CancellationToken ct = default);
}
