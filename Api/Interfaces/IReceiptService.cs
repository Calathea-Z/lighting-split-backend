using Api.Dtos;
using Microsoft.AspNetCore.Http;

public interface IReceiptService
{
    Task<ReceiptSummaryDto> CreateAsync(CreateReceiptDto dto, CancellationToken ct = default);
    Task<ReceiptDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReceiptSummaryDto>> ListAsync(string? ownerUserId, int skip, int take, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ReceiptSummaryDto?> UpdateTotalsAsync(Guid id, UpdateTotalsDto dto, CancellationToken ct = default);

    // Updated: accept the whole upload DTO (so we can capture metadata)
    Task<ReceiptSummaryDto> UploadAsync(UploadReceiptItemDto dto, CancellationToken ct = default);
}
