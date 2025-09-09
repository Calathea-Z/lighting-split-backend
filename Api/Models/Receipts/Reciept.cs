using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Api.Abstractions.Receipts;

namespace Api.Models.Receipts;

[Index(nameof(OwnerUserId), nameof(CreatedAt))]
public class Receipt
{
    // ========= Identity & Ownership =========
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string? OwnerUserId { get; set; }

    [MaxLength(128)]
    public string? IdempotencyKey { get; set; }

    // ========= Storage (Blob) =========
    [Required, Url, MaxLength(2048)]
    public string OriginalFileUrl { get; set; } = "";

    [Required, MaxLength(128)]
    public string BlobContainer { get; set; } = "receipts";

    [Required, MaxLength(256)]
    public string BlobName { get; set; } = ""; // e.g. "{Id}/{rand}.png"

    // ========= OCR =========
    [Column(TypeName = "text")]
    public string? RawText { get; set; }

    // ========= Money (printed totals) =========
    [Precision(18, 2)] public decimal? SubTotal { get; set; }
    [Precision(18, 2)] public decimal? Tax { get; set; }
    [Precision(18, 2)] public decimal? Tip { get; set; }
    [Precision(18, 2)] public decimal? Total { get; set; }

    // ========= Status & Review =========
    [Required]
    public ReceiptStatus Status { get; set; } = ReceiptStatus.PendingParse;

    [MaxLength(512)]
    public string? ParseError { get; set; }

    public bool NeedsReview { get; set; }

    // ========= Reconciliation (transparency) =========
    [Precision(18, 2)] public decimal? ComputedItemsSubtotal { get; set; }
    [Precision(18, 2)] public decimal? BaselineSubtotal { get; set; }
    [Precision(18, 2)] public decimal? Discrepancy { get; set; }

    [MaxLength(256)]
    public string? Reason { get; set; }

    // ========= Parse meta (provenance) =========
    public ParseEngine? ParsedBy { get; set; }
    public bool LlmAttempted { get; set; }                 // default false
    public bool? LlmAccepted { get; set; }                 // null until decided

    [MaxLength(128)]
    public string? LlmModel { get; set; }                  // e.g., "receipt-normalizer-40-mini"

    [MaxLength(64)]
    public string? ParserVersion { get; set; }             // heuristics/version tag

    [MaxLength(512)]
    public string? RejectReason { get; set; }              // why LLM output was rejected

    public DateTime? ParsedAt { get; set; }

    // ========= Audit & Concurrency =========
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Timestamp]
    public uint Version { get; set; }

    // ========= Navigation =========
    public List<ReceiptItem> Items { get; set; } = new();
}
