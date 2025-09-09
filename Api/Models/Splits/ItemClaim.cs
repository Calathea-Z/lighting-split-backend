namespace Api.Models.Splits
{
    public class ItemClaim
    {
        public Guid Id { get; set; }
        public Guid SplitSessionId { get; set; }
        public SplitSession Split { get; set; } = null!;

        public Guid ReceiptItemId { get; set; }    // must belong to Split.ReceiptId
        public Guid ParticipantId { get; set; }

        public decimal QtyShare { get; set; }      // can be fractional (e.g., 0.5)
    }
}
