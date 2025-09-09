using Api.Contracts.Payment;

namespace Api.Dtos.Splits.Responses
{
    public sealed record ShareParticipantDto(
        Guid ParticipantId,
        string DisplayName,
        decimal Total,
        IReadOnlyList<PaymentLink> PaymentLinks,
        bool IsPaid
    );
}
