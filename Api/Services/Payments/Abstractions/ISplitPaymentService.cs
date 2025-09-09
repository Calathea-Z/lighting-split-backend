using Api.Dtos.Splits.Requests;

namespace Api.Services.Payments.Abstractions
{
    public interface ISplitPaymentService
    {
        Task SetAsync(Guid splitId, Guid ownerId, Guid participantId, SetPaymentDto dto);
    }
}
