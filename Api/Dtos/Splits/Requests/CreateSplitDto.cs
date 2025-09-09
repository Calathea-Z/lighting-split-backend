namespace Api.Dtos.Splits.Requests
{
    public sealed record CreateSplitDto(Guid ReceiptId, string? Name);
}
