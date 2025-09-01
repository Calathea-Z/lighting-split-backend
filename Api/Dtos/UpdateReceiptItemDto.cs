using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Api.Dtos;
public sealed class UpdateReceiptItemDto
{
    [Required]
    public uint Version { get; set; }     // required to prevent lost updates

    [MaxLength(200)]
    public string? Label { get; set; }

    [MaxLength(16)]
    public string? Unit { get; set; }

    [MaxLength(64)]
    public string? Category { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public int? Position { get; set; }
    public decimal? Qty { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Discount { get; set; }
    public decimal? Tax { get; set; }
}