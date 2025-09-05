using Api.Dtos.Receipts.Requests.Items;
using Api.Dtos.Receipts.Responses.Items;

namespace Api.Interfaces
{
    public interface IReceiptItemsService
    {
        Task<ReceiptItemDto?> AddItemAsync(Guid receiptId, CreateReceiptItemDto dto, CancellationToken ct = default);
        Task<ReceiptItemDto?> UpdateItemAsync(Guid receiptId, Guid itemId, UpdateReceiptItemDto dto, CancellationToken ct = default);
        Task<bool> DeleteItemAsync(Guid receiptId, Guid itemId, uint? version, CancellationToken ct = default);
    }
}
