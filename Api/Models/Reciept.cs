using System.ComponentModel.DataAnnotations;

namespace Api.Models;

public class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Ownership / correlation
    public string? OwnerUserId { get; set; }

    // Blob metadata (explicit for traceability)
    public string OriginalFileUrl { get; set; } = "";
    public string BlobContainer { get; set; } = "receipts";
    public string BlobName { get; set; } = ""; // e.g. "{Id}/{rand}.png"

    // OCR result (optional full text)
    public string? RawText { get; set; }

    // Money (nullable until parsed/confirmed)
    public decimal? SubTotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Tip { get; set; }
    public decimal? Total { get; set; }

    // Simple status machine
    // e.g., "PendingParse" | "Parsed" | "FailedParse"
    public string Status { get; set; } = "PendingParse";
    public string? ParseError { get; set; }

    // Timestamps
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Optimistic concurrency token (mapped to Postgres xmin)
    [Timestamp]
    public uint Version { get; set; }

    // Nav
    public List<ReceiptItem> Items { get; set; } = [];
}
