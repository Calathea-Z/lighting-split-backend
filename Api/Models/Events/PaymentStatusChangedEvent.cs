namespace Api.Models.Events;

public sealed class PaymentStatusChangedEvent
{
    public Guid SplitResultId { get; set; }
    public Guid ParticipantResultId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public long Version { get; set; }
    public string? ParticipantName { get; set; }
    public decimal Amount { get; set; }
}
