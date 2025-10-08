namespace Api.Models.Owners
{
    public class Owner
    {
        public Guid Id { get; set; }
        public string KeyHash { get; set; } = null!; // base64url(HMAC-SHA256(token))
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastSeenAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public string? LastSeenIp { get; set; }
        public string? LastSeenUserAgent { get; set; }
        public byte TokenVersion { get; set; } = 1;
        public DateTimeOffset IssuedAt { get; set; }

        public ICollection<OwnerPayoutMethod> Methods { get; set; } = new List<OwnerPayoutMethod>();
    }
}
