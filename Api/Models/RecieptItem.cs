using System.ComponentModel.DataAnnotations;

namespace Api.Models;

public class ReceiptItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // FK
    public Guid ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = default!;

    // Descriptive
    [MaxLength(200)]
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
    // Fractional quantities supported (e.g., 0.75 lb)
    public decimal Qty { get; set; } = 1m;       // numeric(9,3)
    public decimal UnitPrice { get; set; } = 0m; // numeric(12,2)

    // Optional per-line adjustments
    public decimal? Discount { get; set; } // amount off; store as positive value
    public decimal? Tax { get; set; }      // per-line tax if available

    // Denormalized line math (computed server-side on create/update)
    public decimal LineSubtotal { get; set; } = 0m; // Qty * UnitPrice - Discount
    public decimal LineTotal { get; set; } = 0m;    // LineSubtotal + Tax

    // Timestamps
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Optimistic concurrency token (mapped to Postgres xmin)
    [Timestamp]
    public uint Version { get; set; }
}
