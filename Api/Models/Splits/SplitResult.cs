namespace Api.Models.Splits
{
    public class SplitResult
    {
        public Guid Id { get; set; }
        public Guid SplitSessionId { get; set; }
        public SplitSession Split { get; set; } = null!;
        public DateTimeOffset CreatedAt { get; set; }
        public ICollection<SplitParticipantResult> Participants { get; set; } = new List<SplitParticipantResult>();
        public ICollection<PaymentStatusEvent> PaymentStatusEvents { get; set; } = new List<PaymentStatusEvent>();
    }
}
