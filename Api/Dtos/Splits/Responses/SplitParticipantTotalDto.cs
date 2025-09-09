namespace Api.Dtos.Splits.Responses
{
    public sealed record SplitParticipantTotalDto(
        Guid ParticipantId,
        string DisplayName,
        decimal ItemsSubtotal,
        decimal DiscountAlloc,
        decimal TaxAlloc,
        decimal TipAlloc,
        decimal Total
    );
}
