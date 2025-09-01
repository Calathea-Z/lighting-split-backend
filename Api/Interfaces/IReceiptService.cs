using Api.Dtos;
using Microsoft.AspNetCore.Http;

public interface IReceiptService
{
    Task<ReceiptSummaryDto> CreateAsync(CreateReceiptDto dto, CancellationToken ct = default);
    Task<ReceiptDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReceiptSummaryDto>> ListAsync(string? ownerUserId, int skip, int take, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ReceiptSummaryDto?> UpdateTotalsAsync(Guid id, UpdateTotalsDto dto, CancellationToken ct = default);
    Task<bool> MarkParseFailedAsync(Guid id, string error, CancellationToken ct = default);

    Task<ReceiptSummaryDto> UploadAsync(UploadReceiptItemDto dto, CancellationToken ct = default);

    Task<ReceiptItemDto?> AddItemAsync(Guid receiptId, CreateReceiptItemDto dto, CancellationToken ct = default);
    Task<ReceiptItemDto?> UpdateItemAsync(Guid receiptId, Guid itemId, UpdateReceiptItemDto dto, CancellationToken ct = default);
    Task<bool> DeleteItemAsync(Guid receiptId, Guid itemId, uint? version, CancellationToken ct = default);
}
