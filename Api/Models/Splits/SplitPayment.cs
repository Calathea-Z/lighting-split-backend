namespace Api.Models.Splits
{

    public class SplitPayment
    {
        public Guid Id { get; set; }
        public Guid SplitSessionId { get; set; }  // FK -> SplitSession
        public Guid ParticipantId { get; set; }   // FK -> SplitParticipant.Id (stable across snapshots)

        public bool IsPaid { get; set; }
        public DateTimeOffset? PaidAt { get; set; }
        public string? PlatformKey { get; set; }  // "venmo"|"cashapp"|...
        public decimal? Amount { get; set; }
        public string? Note { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
