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

        // Payment tracking fields
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
        public DateTimeOffset? PaymentUpdatedAt { get; set; }
        public string? PaymentSource { get; set; }
        public string? PaymentNote { get; set; }
        public Guid? VerifiedByUserId { get; set; }
    }

    public enum PaymentStatus
    {
        Unpaid = 0,
        Paid = 1,
        PartiallyPaid = 2,
        Refunded = 3,
        Disputed = 4,
        SelfReported = 5,
        Verified = 6
    }
}
