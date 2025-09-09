using Api.Contracts.Payment;

namespace Api.Dtos.Splits.Responses
{
    public sealed record FinalizeParticipantDto(
        Guid ParticipantId,
        string DisplayName,
        decimal Total,
        IReadOnlyList<PaymentLink> PaymentLinks);
}
