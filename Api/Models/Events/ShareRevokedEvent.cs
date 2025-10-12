namespace Api.Models.Events;

public sealed class ShareRevokedEvent
{
    public Guid SplitId { get; set; }
    public string ShareCode { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
}
