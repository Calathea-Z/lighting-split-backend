namespace Api.Models.Owners
{
    public class Owner
    {
        public Guid Id { get; set; }
        public string KeyHash { get; set; } = null!; // base64url(SHA-256(token))
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastSeenAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }

        public ICollection<OwnerPayoutMethod> Methods { get; set; } = new List<OwnerPayoutMethod>();
    }
}
