using Api.Dtos;
using Api.Models;

namespace Api.Mappers;

public static class ReceiptMappers
{
    // ---------- Receipt -> DTO ----------

    public static ReceiptSummaryDto ToSummaryDto(this Receipt r)
        => new(
            Id: r.Id,
            Status: r.Status,
            SubTotal: r.SubTotal,
            Tax: r.Tax,
            Tip: r.Tip,
            Total: r.Total,
            CreatedAt: r.CreatedAt,
            UpdatedAt: r.UpdatedAt,
            ItemCount: r.Items?.Count ?? 0
        );

    public static ReceiptDetailDto ToDetailDto(this Receipt r)
        => new(
            Id: r.Id,
            OwnerUserId: r.OwnerUserId,
            Status: r.Status,
            ParseError: r.ParseError,
            OriginalFileUrl: r.OriginalFileUrl,
            BlobContainer: r.BlobContainer,
            BlobName: r.BlobName,
            RawText: r.RawText,
            SubTotal: r.SubTotal,
            Tax: r.Tax,
            Tip: r.Tip,
            Total: r.Total,
            CreatedAt: r.CreatedAt,
            UpdatedAt: r.UpdatedAt,
            Items: (r.Items ?? []).OrderBy(i => i.Position).ThenBy(i => i.Id).Select(ToDto).ToList()
        );

    public static Receipt ToEntity(this CreateReceiptDto dto)
    {
        var r = new Receipt
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Status = "Parsed",
            Items = []
        };

        foreach (var itemDto in dto.Items)
        {
            var i = itemDto.ToEntity(r.Id);
            r.Items.Add(i);
        }

        return r;
    }

    public static ReceiptItemDto ToDto(this ReceiptItem i)
        => new(
            Id: i.Id,
            Label: i.Label,
            Unit: i.Unit,
            Category: i.Category,
            Notes: i.Notes,
            Position: i.Position,
            Qty: i.Qty,
            UnitPrice: i.UnitPrice,
            Discount: i.Discount,
            Tax: i.Tax,
            LineSubtotal: i.LineSubtotal,
            LineTotal: i.LineTotal,
            CreatedAt: i.CreatedAt,
            UpdatedAt: i.UpdatedAt,
            Version: i.Version
        );

    // ---------- DTO -> ReceiptItem (create/update) ----------

    public static ReceiptItem ToEntity(this CreateReceiptItemDto dto, Guid receiptId)
    {
        var i = new ReceiptItem
        {
            ReceiptId = receiptId,
            Label = dto.Label?.Trim() ?? "",
            Unit = string.IsNullOrWhiteSpace(dto.Unit) ? null : dto.Unit!.Trim(),
            Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category!.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes!.Trim(),
            Position = dto.Position,
            Qty = dto.Qty,
            UnitPrice = dto.UnitPrice,
            Discount = dto.Discount,
            Tax = dto.Tax,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Recalculate(i);
        return i;
    }

    // Applies a partial update and recalculates line math.
    // Caller should set i.Version from DB (tracked by EF); EF will enforce concurrency via xmin.
    public static void ApplyUpdate(this ReceiptItem i, UpdateReceiptItemDto dto)
    {
        // NOTE: We don't set i.Version here; EF checks it on SaveChanges.
        if (dto.Label is not null) i.Label = dto.Label.Trim();
        if (dto.Unit is not null) i.Unit = string.IsNullOrWhiteSpace(dto.Unit) ? null : dto.Unit.Trim();
        if (dto.Category is not null) i.Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        if (dto.Notes is not null) i.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

        if (dto.Position.HasValue) i.Position = dto.Position.Value;
        if (dto.Qty.HasValue) i.Qty = dto.Qty.Value;
        if (dto.UnitPrice.HasValue) i.UnitPrice = dto.UnitPrice.Value;
        if (dto.Discount.HasValue) i.Discount = dto.Discount.Value;
        if (dto.Tax.HasValue) i.Tax = dto.Tax.Value;

        i.UpdatedAt = DateTimeOffset.UtcNow;
        Recalculate(i);
    }

    // ---------- Helpers ----------

    // Centralized line-math rule: never negative subtotal.
    public static void Recalculate(ReceiptItem i)
    {
        var subtotal = (i.Qty * i.UnitPrice) - (i.Discount ?? 0m);
        i.LineSubtotal = subtotal < 0m ? 0m : subtotal;
        i.LineTotal = i.LineSubtotal + (i.Tax ?? 0m);
    }

    // Safe rollups used by services when caller omits totals.
    public static (decimal? sub, decimal? tax, decimal? total) Rollup(this Receipt r)
    {
        var items = r.Items ?? [];
        var sub = items.Sum(x => x.LineSubtotal);
        var tax = items.Sum(x => x.Tax ?? 0m);
        var tot = items.Sum(x => x.LineTotal);
        return (sub == 0m ? null : sub, tax == 0m ? null : tax, tot == 0m ? null : tot);
    }
}
