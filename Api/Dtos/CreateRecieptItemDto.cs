using System.ComponentModel.DataAnnotations;

namespace Api.Dtos;


// Create / Update DTOs for items
public sealed class CreateReceiptItemDto
{
    [Required, MaxLength(200)]
    public string Label { get; set; } = "";

    [MaxLength(16)]
    public string? Unit { get; set; }

    [MaxLength(64)]
    public string? Category { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    // Defaults mirror model defaults
    public int Position { get; set; } = 0;
    public decimal Qty { get; set; } = 1m;
    public decimal UnitPrice { get; set; } = 0m;

    public decimal? Discount { get; set; }
    public decimal? Tax { get; set; }
}