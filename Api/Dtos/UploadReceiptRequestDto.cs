namespace Api.Dtos;

using Microsoft.AspNetCore.Http;

public sealed class UploadReceiptRequestDto
{
    // The input name in Swagger/UI will be "file"
    public required IFormFile File { get; init; }

    // Any extra form fields you want to send alongside the file:
    public string? StoreName { get; init; }
    public DateTimeOffset? PurchasedAt { get; init; }
    public string? Notes { get; init; }
}