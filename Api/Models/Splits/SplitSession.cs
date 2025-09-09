namespace Api.Models.Splits
{
    public class SplitSession
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }       // FK -> Owners.Id (AOK owner)
        public Guid ReceiptId { get; set; }     // FK -> Receipts.Id
        public string? Name { get; set; }

        public bool IsFinalized { get; set; }
        public string? ShareCode { get; set; }
        public DateTimeOffset? FinalizedAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public ICollection<SplitParticipant> Participants { get; set; } = new List<SplitParticipant>();
        public ICollection<ItemClaim> Claims { get; set; } = new List<ItemClaim>();
    }
}
