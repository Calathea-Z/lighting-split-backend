namespace Api.Models.Splits
{
    /// <summary>
    /// Audit event for tracking payment status changes on split participant results
    /// Scoped to the snapshot to maintain immutable payment history
    /// </summary>
    public class PaymentStatusEvent
    {
        public Guid Id { get; set; }
        public Guid SplitResultId { get; set; }
        public SplitResult SplitResult { get; set; } = null!;
        public Guid SplitParticipantResultId { get; set; }
        public SplitParticipantResult SplitParticipantResult { get; set; } = null!;
        public PaymentStatus OldStatus { get; set; }
        public PaymentStatus NewStatus { get; set; }
        public string Source { get; set; } = null!; // e.g., "manual", "venmo", "cashapp", "system"
        public Guid? ActorId { get; set; } // User who made the change (null for system/automatic)
        public DateTimeOffset Timestamp { get; set; }
        public string? CorrelationId { get; set; } // For tracking related events
        public string? Notes { get; set; } // Additional context about the change
    }
}
