using Api.Contracts.Payment;
using Api.Data;
using Api.Dtos.Splits.Responses;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Api.Services.Payments;

public sealed class SplitCalculator : ISplitCalculator
{
    private readonly LightningDbContext _db;
    public SplitCalculator(LightningDbContext db) => _db = db;

    private static decimal Round4(decimal d) => Math.Round(d, 4, MidpointRounding.AwayFromZero);

    public async Task<SplitPreviewDto> PreviewAsync(Guid splitId)
    {
        var split = await _db.SplitSessions
            .Include(s => s.Participants)
            .Include(s => s.Claims)
            .FirstOrDefaultAsync(s => s.Id == splitId);
        if (split is null) throw new KeyNotFoundException("Split not found.");

        var receipt = await _db.Receipts
            .Include(r => r.Items)
            .FirstAsync(r => r.Id == split.ReceiptId);

        var parts = split.Participants.ToDictionary(p => p.Id);
        var totals = parts.Values.ToDictionary(
            p => p.Id,
            p => new MutableTotal { ParticipantId = p.Id, DisplayName = p.DisplayName });

        // Map claims per item
        var claimsByItem = split.Claims.GroupBy(c => c.ReceiptItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 1) Per-item allocation on LineSubtotal
        foreach (var item in receipt.Items)
        {
            claimsByItem.TryGetValue(item.Id, out var claimsForItem);
            decimal totalClaimed = claimsForItem?.Sum(c => c.QtyShare) ?? 0m;
            if (totalClaimed <= 0m) continue;

            var itemSubtotal = item.LineSubtotal; // pre-tax/tip
            foreach (var c in claimsForItem!)
            {
                var frac = (totalClaimed == 0m) ? 0m : (c.QtyShare / totalClaimed);
                var alloc = Round4(itemSubtotal * frac);
                totals[c.ParticipantId].ItemsSubtotal += alloc;
            }
        }

        // 2) Unclaimed items (qty left)
        var unclaimed = new List<SplitUnclaimedItemDto>();
        foreach (var item in receipt.Items)
        {
            claimsByItem.TryGetValue(item.Id, out var claimsForItem);
            decimal claimed = claimsForItem?.Sum(c => c.QtyShare) ?? 0m;
            decimal left = item.Qty - claimed;
            if (left > 0m) unclaimed.Add(new SplitUnclaimedItemDto(item.Id, left));
        }

        // 3) Discount/surcharge pool between sum(LineSubtotal) and Receipt.SubTotal
        decimal itemsSubtotalSum = Round4(receipt.Items.Sum(i => i.LineSubtotal));
        decimal receiptSubtotal = receipt.SubTotal ?? itemsSubtotalSum;
        decimal discountPool = Round4(receiptSubtotal - itemsSubtotalSum); // (+) surcharge, (-) discount)

        if (discountPool != 0m)
        {
            var baseSum = totals.Values.Sum(t => t.ItemsSubtotal);
            if (baseSum != 0m)
            {
                foreach (var t in totals.Values)
                {
                    var share = t.ItemsSubtotal / baseSum;
                    var alloc = Round4(discountPool * share);
                    t.DiscountAlloc += alloc; // may be negative (discount)
                    t.ItemsSubtotal += alloc; // adjust subtotal by pool
                }
            }
        }

        // 4) Tax & Tip proportional to (adjusted) items subtotal
        decimal tax = receipt.Tax ?? 0m;
        decimal tip = receipt.Tip ?? 0m;

        var baseForAlloc = totals.Values.Sum(t => t.ItemsSubtotal);
        if (baseForAlloc > 0m)
        {
            foreach (var t in totals.Values)
            {
                var share = t.ItemsSubtotal / baseForAlloc;
                t.TaxAlloc += Round4(tax * share);
                t.TipAlloc += Round4(tip * share);
            }
        }

        // 5) Round to cents and reconcile remainder to max-total participant
        var exactTotals = totals.Values.ToDictionary(
            t => t.ParticipantId,
            t => t.ItemsSubtotal + t.TaxAlloc + t.TipAlloc);

        var roundedTotals = totals.Values.ToDictionary(
            t => t.ParticipantId,
            t => Math.Round(exactTotals[t.ParticipantId], 2, MidpointRounding.AwayFromZero));

        var sumRounded = roundedTotals.Values.Sum();
        var desired = Math.Round(receiptSubtotal + tax + tip, 2, MidpointRounding.AwayFromZero);
        var remainder = desired - sumRounded;

        if (remainder != 0m && totals.Count > 0)
        {
            var maxP = roundedTotals.OrderByDescending(kv => kv.Value).First().Key;
            roundedTotals[maxP] = Math.Round(roundedTotals[maxP] + remainder, 2, MidpointRounding.AwayFromZero);
        }

        // Build DTOs
        var dtoParts = totals.Values
            .Select(t => new SplitParticipantTotalDto(
                t.ParticipantId,
                t.DisplayName,
                Math.Round(t.ItemsSubtotal, 2, MidpointRounding.AwayFromZero),
                Math.Round(t.DiscountAlloc, 2, MidpointRounding.AwayFromZero),
                Math.Round(t.TaxAlloc, 2, MidpointRounding.AwayFromZero),
                Math.Round(t.TipAlloc, 2, MidpointRounding.AwayFromZero),
                roundedTotals[t.ParticipantId]))
            .OrderBy(x => parts[x.ParticipantId].SortOrder)
            .ToList();

        return new SplitPreviewDto(
            split.Id,
            Math.Round(receiptSubtotal, 2, MidpointRounding.AwayFromZero),
            Math.Round(tax, 2, MidpointRounding.AwayFromZero),
            Math.Round(tip, 2, MidpointRounding.AwayFromZero),
            Math.Round(desired, 2, MidpointRounding.AwayFromZero),
            dtoParts,
            unclaimed,
            Math.Round(desired - dtoParts.Sum(p => p.Total), 2, MidpointRounding.AwayFromZero)
        );
    }
}
