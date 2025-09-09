using Api.Dtos.Splits.Responses;

namespace Api.Services.Payments.Abstractions
{

    public interface ISplitShareReader
    {
        Task<ShareSplitResponseDto> GetByCodeAsync(string code);
    }
}
