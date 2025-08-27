using Api.Dtos;
using Api.Models;

namespace Api.Mapping;

public static class ReceiptMapping
{
    public static Receipt ToEntity(this CreateReceiptDto dto)
    {
        var receipt = new Receipt
        {
            OwnerUserId = dto.OwnerUserId,
            OriginalFileUrl = dto.OriginalFileUrl ?? string.Empty,
            RawText = dto.RawText ?? string.Empty,
            Items = dto.Items?.Select(i => new ReceiptItem
            {
                Label = i.Label,
                Qty = i.Qty,
                UnitPrice = i.UnitPrice
            }).ToList() ?? []
        };

        // Compute SubTotal now; Tax/Tip may be set later by parser
        var subtotal = receipt.Items.Sum(i => i.UnitPrice * i.Qty);
        receipt.SubTotal = subtotal;
        receipt.Total = subtotal; // until Tax/Tip are populated
        return receipt;
    }

    public static ReceiptSummaryDto ToSummaryDto(this Receipt r) =>
        new(
            r.Id,
            r.OwnerUserId,
            r.OriginalFileUrl,
            r.CreatedAt,
            r.SubTotal,
            r.Tax,
            r.Tip,
            r.Total,
            r.Items.Count
        );

    public static ReceiptDetailDto ToDetailDto(this Receipt r) =>
        new(
            r.Id,
            r.OwnerUserId,
            r.OriginalFileUrl,
            r.RawText,
            r.CreatedAt,
            r.SubTotal,
            r.Tax,
            r.Tip,
            r.Total,
            [.. r.Items.Select(i => new ReceiptItemDto(i.Id, i.Label, i.Qty, i.UnitPrice))]
        );
}
