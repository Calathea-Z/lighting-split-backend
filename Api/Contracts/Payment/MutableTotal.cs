namespace Api.Contracts.Payment
{
    public sealed class MutableTotal
    {
        public Guid ParticipantId { get; init; }
        public string DisplayName { get; init; } = "";
        public decimal ItemsSubtotal;
        public decimal DiscountAlloc;
        public decimal TaxAlloc;
        public decimal TipAlloc;
    }
}
