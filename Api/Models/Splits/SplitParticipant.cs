namespace Api.Models.Splits
{
    public class SplitParticipant
    {
        public Guid Id { get; set; }
        public Guid SplitSessionId { get; set; }
        public SplitSession Split { get; set; } = null!;

        public string DisplayName { get; set; } = null!;
        public int SortOrder { get; set; } = 0;
    }
}
