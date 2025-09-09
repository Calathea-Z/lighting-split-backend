using Api.Dtos.Receipts.Requests;
using Api.Dtos.Receipts.Responses;
using Api.Abstractions.Receipts;
using Api.Models.Receipts;

namespace Api.Mappers;

public static class ReceiptMaps
{
    // ---------- Receipt -> DTO ----------
    public static ReceiptSummaryDto ToSummaryDto(this Receipt r) =>
        new(
            r.Id,
            r.Status,
            r.SubTotal,
            r.Tax,
            r.Tip,
            r.Total,
            r.CreatedAt,
            r.UpdatedAt,
            r.Items?.Count ?? 0,
            r.ComputedItemsSubtotal,
            r.BaselineSubtotal,
            r.Discrepancy,
            r.Reason,
            r.NeedsReview
        );

    public static ReceiptDetailDto ToDetailDto(this Receipt r) =>
        new(
            r.Id,
            r.Status,
            r.RawText,
            r.SubTotal,
            r.Tax,
            r.Tip,
            r.Total,
            r.CreatedAt,
            r.UpdatedAt,
            r.ComputedItemsSubtotal,
            r.BaselineSubtotal,
            r.Discrepancy,
            r.Reason,
            r.NeedsReview,
            (r.Items ?? []).OrderBy(i => i.Position).Select(i => i.ToDto()).ToList()
        );

    // ---------- DTO -> Receipt ----------
    public static Receipt ToEntity(this CreateReceiptDto dto)
    {
        var r = new Receipt
        {
            RawText = dto.RawText?.Trim(),
            SubTotal = Money.Round2(dto.SubTotal),
            Tax = Money.Round2(dto.Tax),
            Tip = Money.Round2(dto.Tip),
            Total = Money.Round2(dto.Total),
            // StoreName / PurchasedAt / Notes: add columns before persisting
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        r.Items = dto.Items.Select(i => i.ToEntity(r.Id)).ToList();
        return r;
    }

    // ---------- Helpers ----------
    /// Safe rollups when header totals are omitted; returns nulls if sums are zero.
    public static (decimal? sub, decimal? tax, decimal? total) Rollup(this Receipt r)
    {
        var items = r.Items ?? [];
        var sub = items.Sum(x => x.LineSubtotal);
        var tax = items.Sum(x => x.Tax ?? 0m);
        var tot = items.Sum(x => x.LineTotal);

        sub = Money.Round2(sub);
        tax = Money.Round2(tax);
        tot = Money.Round2(tot);

        return (sub == 0m ? null : sub, tax == 0m ? null : tax, tot == 0m ? null : tot);
    }
}
