using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Api.Models.Receipts;

[Index(nameof(ReceiptId), nameof(Position))]
public class ReceiptItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Foreign key
    [Required]
    public Guid ReceiptId { get; set; }

    public Receipt Receipt { get; set; } = default!;

    // Descriptive
    [Required, MaxLength(200)]
    public string Label { get; set; } = "";

    [MaxLength(16)]
    public string? Unit { get; set; } // "ea","lb","oz","kg", etc.

    [MaxLength(64)]
    public string? Category { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    // Stable ordering within a receipt
    public int Position { get; set; } = 0;

    // Quantities & money
    [Precision(9, 3)]
    public decimal Qty { get; set; } = 1m;  // fractional quantities supported

    [Precision(12, 2)]
    public decimal UnitPrice { get; set; } = 0m;

    // Optional per-line adjustments
    [Precision(12, 2)]
    public decimal? Discount { get; set; } // amount off; store as positive value

    [Precision(12, 2)]
    public decimal? Tax { get; set; } // per-line tax if available

    // Denormalized line math (computed server-side on create/update)
    [Precision(12, 2)]
    public decimal LineSubtotal { get; set; } = 0m; // Qty * UnitPrice - Discount

    [Precision(12, 2)]
    public decimal LineTotal { get; set; } = 0m; // LineSubtotal + Tax

    // Timestamps
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Optimistic concurrency token (mapped to Postgres xmin)
    [Timestamp]
    public uint Version { get; set; }

    /// <summary>
    /// True if this was system-generated (e.g. "Adjustment" line).
    /// </summary>
    public bool IsSystemGenerated { get; set; } = false;
}
