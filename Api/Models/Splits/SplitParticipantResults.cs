namespace Api.Models.Splits
{
    public class SplitParticipantResult
    {
        public Guid Id { get; set; }
        public Guid SplitResultId { get; set; }
        public SplitResult Result { get; set; } = null!;
        public Guid ParticipantId { get; set; }
        public string DisplayName { get; set; } = null!;
        public decimal ItemsSubtotal { get; set; }
        public decimal DiscountAlloc { get; set; }
        public decimal TaxAlloc { get; set; }
        public decimal TipAlloc { get; set; }
        public decimal Total { get; set; }
    }
}
