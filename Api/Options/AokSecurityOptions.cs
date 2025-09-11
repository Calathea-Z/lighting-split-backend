namespace Api.Options
{
    public sealed class AokSecurityOptions
    {
        /// <summary>
        /// Base64-encoded pepper used as the HMAC key. Must be at least 32 bytes (256 bits).
        /// Store in Key Vault and load into configuration at startup.
        /// </summary>
        public string PepperBase64 { get; set; } = "";
    }
}
