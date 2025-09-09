namespace Api.Dtos.Splits.Common
{
    public sealed record ItemClaimDto(Guid ReceiptItemId, Guid ParticipantId, decimal QtyShare);
}
