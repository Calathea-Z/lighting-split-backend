namespace Api.Dtos.Aok;

public sealed class AokStatusDto
{
    public Guid OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset TokenIssuedAt { get; set; }
    public byte TokenVersion { get; set; }
    public int DaysUntilExpiration { get; set; }
    public bool WillRotateSoon { get; set; }
    public bool IsRevoked { get; set; }
    public int PayoutMethodCount { get; set; }
    public string? LastSeenIp { get; set; }
    public string? LastSeenUserAgent { get; set; }
}

