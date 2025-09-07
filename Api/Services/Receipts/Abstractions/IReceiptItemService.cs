using Api.Dtos.Receipts.Requests.Items;
using Api.Dtos.Receipts.Responses.Items;
using Api.Abstractions.Transport;

namespace Api.Services.Receipts.Abstractions
{
    public interface IReceiptItemsService
    {
        Task<ReceiptItemDto?> AddItemAsync(Guid receiptId, CreateReceiptItemRequest dto, CancellationToken ct = default);
        Task<ReceiptItemDto?> UpdateItemAsync(Guid receiptId, Guid itemId, UpdateReceiptItemDto dto, CancellationToken ct = default);
        Task<bool> DeleteItemAsync(Guid receiptId, Guid itemId, uint? version, CancellationToken ct = default);
    }
}
