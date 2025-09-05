using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Api.Abstractions.Receipts;

namespace Api.Models;

[Index(nameof(OwnerUserId), nameof(CreatedAt))]
public class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Ownership / correlation
    [MaxLength(64)]
    public string? OwnerUserId { get; set; }

    // Blob metadata
    [Required, Url, MaxLength(2048)]
    public string OriginalFileUrl { get; set; } = "";

    [Required, MaxLength(128)]
    public string BlobContainer { get; set; } = "receipts";

    [Required, MaxLength(256)]
    public string BlobName { get; set; } = ""; // e.g. "{Id}/{rand}.png"

    // OCR result (optional full text)
    public string? RawText { get; set; } // map to text in EF

    // Money (nullable until parsed/confirmed)
    [Precision(18, 2)] public decimal? SubTotal { get; set; }
    [Precision(18, 2)] public decimal? Tax { get; set; }
    [Precision(18, 2)] public decimal? Tip { get; set; }
    [Precision(18, 2)] public decimal? Total { get; set; }

    // Status
    [Required]
    public ReceiptStatus Status { get; set; } = ReceiptStatus.PendingParse;

    [MaxLength(512)]
    public string? ParseError { get; set; }

    // Timestamps
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Optimistic concurrency
    [Timestamp]
    public uint Version { get; set; }

    // Reconciliation transparency
    [Precision(18, 2)] public decimal? ComputedItemsSubtotal { get; set; }
    [Precision(18, 2)] public decimal? BaselineSubtotal { get; set; }
    [Precision(18, 2)] public decimal? Discrepancy { get; set; }

    [MaxLength(256)]
    public string? Reason { get; set; }

    public bool NeedsReview { get; set; }

    // Nav
    public List<ReceiptItem> Items { get; set; } = new();
}
