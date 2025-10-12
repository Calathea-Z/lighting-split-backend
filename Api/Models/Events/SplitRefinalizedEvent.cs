namespace Api.Models.Events;

public sealed class SplitRefinalizedEvent
{
    public Guid SplitId { get; set; }
    public Guid SplitResultId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
