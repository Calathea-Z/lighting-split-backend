using System.Runtime.InteropServices;

namespace Api.Models.Owners
{

    public class OwnerPayoutMethod
    {
        public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Owner Owner { get; set; } = null!;

        public int PlatformId { get; set; }
        public PayoutPlatform Platform { get; set; } = null!;

        public string HandleOrUrl { get; set; } = null!;
        public string? DisplayLabel { get; set; }
        public string? QrImageBlobPath { get; set; }
        public bool IsDefault { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
