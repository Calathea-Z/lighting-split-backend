using Api.Dtos.Receipts.Requests.Items;
using Api.Dtos.Receipts.Responses.Items;
using Api.Models;

namespace Api.Mappers;

public static class ReceiptItemMaps
{
    // ---------- ReceiptItem -> DTO ----------
    public static ReceiptItemDto ToDto(this ReceiptItem i) =>
        new(
            i.Id,
            i.ReceiptId,
            i.Label,
            i.Unit,
            i.Category,
            i.Notes,
            i.Position,
            i.Qty,
            i.UnitPrice,
            i.Discount,
            i.Tax,
            i.LineSubtotal,
            i.LineTotal,
            i.Version,
            i.IsSystemGenerated
        );

    // ---------- DTO -> ReceiptItem ----------
    public static ReceiptItem ToEntity(this CreateReceiptItemDto dto, Guid receiptId)
    {
        var i = new ReceiptItem
        {
            ReceiptId = receiptId,
            Label = (dto.Label ?? "").Trim(),
            Unit = string.IsNullOrWhiteSpace(dto.Unit) ? null : dto.Unit.Trim(),
            Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            Position = dto.Position,
            Qty = Quant.Round3(dto.Qty <= 0 ? 1m : dto.Qty),
            UnitPrice = Money.Round2(dto.UnitPrice),
            Discount = Money.Round2(dto.Discount),
            Tax = Money.Round2(dto.Tax),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        Recalculate(i);
        return i;
    }

    // Partial update + recalculation. EF handles concurrency via xmin.
    public static void ApplyUpdate(this ReceiptItem i, UpdateReceiptItemDto dto)
    {
        if (dto.Label is not null) i.Label = dto.Label.Trim();
        if (dto.Unit is not null) i.Unit = string.IsNullOrWhiteSpace(dto.Unit) ? null : dto.Unit.Trim();
        if (dto.Category is not null) i.Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        if (dto.Notes is not null) i.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

        if (dto.Position is not null) i.Position = dto.Position.Value;
        if (dto.Qty is not null) i.Qty = Quant.Round3(dto.Qty.Value);
        if (dto.UnitPrice is not null) i.UnitPrice = Money.Round2(dto.UnitPrice.Value);
        if (dto.Discount is not null) i.Discount = Money.Round2(dto.Discount.Value);
        if (dto.Tax is not null) i.Tax = Money.Round2(dto.Tax.Value);

        i.UpdatedAt = DateTimeOffset.UtcNow;
        Recalculate(i);
    }

    // ---------- Line math (DB-precision safe) ----------
    public static void Recalculate(ReceiptItem i)
    {
        var subtotal = i.Qty * i.UnitPrice;
        var afterDiscount = subtotal - (i.Discount ?? 0m);
        i.LineSubtotal = Money.Round2(afterDiscount < 0m ? 0m : afterDiscount);
        i.LineTotal = Money.Round2(i.LineSubtotal + (i.Tax ?? 0m));
    }
}

internal static class Money
{
    // 2dp, away-from-zero to match common receipts; change to ToEven if desired.
    public static decimal Round2(decimal? v) =>
        v is null ? v ?? 0m : decimal.Round(v.Value, 2, MidpointRounding.AwayFromZero);
    public static decimal Round2(decimal v) =>
        decimal.Round(v, 2, MidpointRounding.AwayFromZero);
}

internal static class Quant
{
    // quantities to 3dp
    public static decimal Round3(decimal v) =>
        decimal.Round(v, 3, MidpointRounding.AwayFromZero);
}
