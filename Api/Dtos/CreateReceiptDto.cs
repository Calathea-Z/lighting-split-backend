using System.ComponentModel.DataAnnotations;

namespace Api.Dtos;

public sealed class CreateReceiptDto
{
    [Required]
    public List<CreateReceiptItemDto> Items { get; set; } = [];
}