namespace Api.Services.Payments.Abstractions
{
    public interface IShareCodeService
    {
        Task<string> GenerateUniqueAsync(int len = 8, CancellationToken ct = default);
    }
}
