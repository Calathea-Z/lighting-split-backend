using Api.Contracts.Payment;
using Api.Models.Splits;

namespace Api.Dtos.Splits.Responses
{
    public sealed record FinalizeParticipantDto(
        Guid ParticipantId,
        string DisplayName,
        decimal Total,
        IReadOnlyList<PaymentLink> PaymentLinks,
        PaymentStatus PaymentStatus,
        DateTimeOffset? PaymentUpdatedAt,
        string? PaymentSource,
        string? PaymentNote);
}
