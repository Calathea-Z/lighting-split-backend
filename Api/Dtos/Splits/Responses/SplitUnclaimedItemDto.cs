namespace Api.Dtos.Splits.Responses
{
    public sealed record SplitUnclaimedItemDto(Guid ReceiptItemId, decimal UnclaimedQty);
}
