namespace Api.Dtos;

using Microsoft.AspNetCore.Http;

public sealed class UploadReceiptItemDto
{
    public required IFormFile File { get; init; }
    public string? StoreName { get; init; }
    public DateTimeOffset? PurchasedAt { get; init; }
    public string? Notes { get; init; }
}