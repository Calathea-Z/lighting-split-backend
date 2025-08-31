using Api.Dtos;

namespace Api.Interfaces;

public interface IReceiptService
{
    Task<ReceiptSummaryDto> CreateAsync(CreateReceiptDto dto, CancellationToken ct = default);
    Task<ReceiptDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReceiptSummaryDto>> ListAsync(string? ownerUserId, int skip, int take, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ReceiptSummaryDto?> UpdateTotalsAsync(Guid id, UpdateTotalsDto dto, CancellationToken ct = default);
    Task<ReceiptSummaryDto> UploadAsync(IFormFile file, CancellationToken ct = default);
}