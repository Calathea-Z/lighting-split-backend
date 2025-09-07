using Api.Abstractions.Transport;
using Api.Dtos.Receipts.Requests;
using Api.Dtos.Receipts.Responses;
using Api.Dtos.Receipts.Responses.Items;

namespace Api.Services.Receipts.Abstractions;

public interface IReceiptService
{
    Task<ReceiptSummaryDto> CreateAsync(CreateReceiptDto dto, CancellationToken ct = default);
    Task<ReceiptDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<ReceiptSummaryDto>> ListAsync(string? ownerUserId, int skip, int take, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ReceiptSummaryDto?> UpdateTotalsAsync(Guid id, UpdateTotalsRequest dto, CancellationToken ct = default);
    Task<bool> MarkParseFailedAsync(Guid id, string error, CancellationToken ct = default);
    Task<ReceiptSummaryDto> UploadAsync(UploadReceiptItemDto dto, string? idempotencyKey, CancellationToken ct = default);
    Task<ReceiptSummaryDto?> UpdateRawTextAsync(Guid id, UpdateRawTextRequest dto, CancellationToken ct = default);
    Task<ReceiptSummaryDto?> UpdateStatusAsync(Guid id, UpdateStatusRequest dto, CancellationToken ct = default);
    Task<ReceiptSummaryDto?> UpdateReviewAsync(Guid id, UpdateReviewDto dto, CancellationToken ct = default);
    Task<bool> UpdateParseMetaAsync(Guid id, UpdateParseMetaRequest req, CancellationToken ct = default);
}
