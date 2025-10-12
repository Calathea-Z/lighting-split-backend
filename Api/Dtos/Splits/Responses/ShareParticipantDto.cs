using Api.Contracts.Payment;
using Api.Models.Splits;

namespace Api.Dtos.Splits.Responses
{
    public sealed record ShareParticipantDto(
        Guid ParticipantId,
        string DisplayName,
        decimal Total,
        IReadOnlyList<PaymentLink> PaymentLinks,
        bool IsPaid,
        PaymentStatus PaymentStatus,
        DateTimeOffset? PaymentUpdatedAt,
        string? PaymentSource,
        string? PaymentNote,
        string? ParticipantToken // Token for self-reporting payment
    );
}
