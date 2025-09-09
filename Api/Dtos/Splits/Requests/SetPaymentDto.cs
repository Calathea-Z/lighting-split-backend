namespace Api.Dtos.Splits.Requests
{
    public sealed record SetPaymentDto(
        bool IsPaid,
        string? PlatformKey,
        decimal? Amount,
        string? Note
    );
}
