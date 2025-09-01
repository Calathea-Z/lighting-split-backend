namespace Api.Options
{
    public sealed class StorageOptions
    {
        // e.g. "receipts" (dev), "receipts-uat", "receipts-prod"
        public string ReceiptsContainer { get; set; } = "receipts";

        // Optional: lets you switch to immutability or different visibility by env
        public bool OverwriteOnUpload { get; set; } = true;
    }
}
