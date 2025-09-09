using Api.Dtos.Splits.Responses;

namespace Api.Services.Payments.Abstractions
{
    public interface ISplitCalculator
    {
        Task<SplitPreviewDto> PreviewAsync(Guid splitId);
    }
}
