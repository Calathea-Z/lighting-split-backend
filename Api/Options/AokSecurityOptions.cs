namespace Api.Options
{
    public sealed class AokSecurityOptions
    {
        /// <summary>
        /// Base64-encoded pepper used as the HMAC key. Must be at least 32 bytes (256 bits).
        /// Store in Key Vault and load into configuration at startup.
        /// </summary>
        public string PepperBase64 { get; set; } = "";

        /// <summary>
        /// Token version (1-255). Increment when rotating secrets or changing token format.
        /// Old versions can still be validated during a grace period.
        /// </summary>
        public byte TokenVersion { get; set; } = 1;

        /// <summary>
        /// Maximum age of a token before it requires rotation (in days).
        /// Tokens older than this will be rejected (replay protection).
        /// Default: 90 days.
        /// </summary>
        public int MaxTokenAgeDays { get; set; } = 90;

        /// <summary>
        /// Grace period for accepting old token versions after rotation (in days).
        /// Default: 7 days.
        /// </summary>
        public int VersionGracePeriodDays { get; set; } = 7;

        /// <summary>
        /// Days before expiration to proactively rotate token.
        /// Default: 30 days (rotate when token has 30 days left).
        /// </summary>
        public int ProactiveRotationThresholdDays { get; set; } = 30;
    }
}
