using Api.Dtos.Splits.Responses;

namespace Api.Services.Payments.Abstractions
{
    public interface ISplitFinalizerService
    {
        Task<FinalizeSplitResponse> FinalizeAsync(Guid splitId, Guid ownerId, string baseUrl);
    }
}
