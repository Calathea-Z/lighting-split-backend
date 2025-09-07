using Api.Common.Interfaces;
using Api.Data;
using Api.Dtos.Receipts.Requests.Items;
using Api.Dtos.Receipts.Responses.Items;
using Api.Mappers;
using Api.Models;
using Api.Services.Receipts.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Api.Services.Receipts;

public sealed class ReceiptItemsService : IReceiptItemsService
{
    private readonly LightningDbContext _db;
    private readonly IClock _clock;
    private readonly IReceiptReconciliationOrchestrator _reconciler;

    public ReceiptItemsService(
        LightningDbContext db,
        IClock clock,
        IReceiptReconciliationOrchestrator reconciler)
    {
        _db = db;
        _clock = clock;
        _reconciler = reconciler;
    }

    public async Task<ReceiptItemDto?> AddItemAsync(Guid receiptId, CreateReceiptItemDto dto, CancellationToken ct = default)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        var exists = await _db.Receipts.AnyAsync(r => r.Id == receiptId, ct);
        if (!exists) return null;

        // Basic numeric guards
        if (dto.Qty <= 0m) throw new ArgumentException("Qty must be > 0.", nameof(dto.Qty));
        if (dto.UnitPrice < 0m) throw new ArgumentException("UnitPrice cannot be negative.", nameof(dto.UnitPrice));
        if (dto.Discount is < 0m) throw new ArgumentException("Discount cannot be negative.", nameof(dto.Discount));
        if (dto.Tax is < 0m) throw new ArgumentException("Tax cannot be negative.", nameof(dto.Tax));

        // Compute next position: if dto.Position < 1, use max+1
        var maxPos = await _db.ReceiptItems
            .Where(x => x.ReceiptId == receiptId)
            .Select(x => (int?)x.Position)
            .MaxAsync(ct);

        int nextPos = (dto.Position >= 1) ? dto.Position : ((maxPos ?? 0) + 1);

        // Normalize label by removing qty tokens that match provided qty (e.g., "Coffee 1x" → "Coffee")
        var normalizedLabel = NormalizeLabelByQty((dto.Label ?? string.Empty).Trim(), dto.Qty);

        // Block user-created "Adjustment" rows (system-managed)
        if (string.Equals(normalizedLabel, "Adjustment", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The 'Adjustment' line is system-managed and cannot be created manually.");

        // Block totals/promo/meta rows as user items (keep item list to purchasables)
        if (LooksLikeNonItem(normalizedLabel))
            throw new InvalidOperationException("Labels like Subtotal/Tax/Tip/Discount/Promo are totals/meta and cannot be added as items.");

        var item = new ReceiptItem
        {
            ReceiptId = receiptId,
            Label = normalizedLabel,
            Unit = string.IsNullOrWhiteSpace(dto.Unit) ? null : dto.Unit.Trim(),
            Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim(),
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            Position = nextPos,
            Qty = NormalizeQty(dto.Qty),
            UnitPrice = Round2(dto.UnitPrice),
            Discount = dto.Discount is null ? null : Round2(dto.Discount.Value),
            Tax = dto.Tax is null ? null : Round2(dto.Tax.Value),
            IsSystemGenerated = false,
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow
        };

        // Clamp discount ≤ (qty * unit)
        var maxDiscount = Round2(item.Qty * item.UnitPrice);
        if (item.Discount is > 0m && item.Discount > maxDiscount) item.Discount = maxDiscount;

        ReceiptItemMaps.Recalculate(item);

        _db.ReceiptItems.Add(item);
        await _db.SaveChangesAsync(ct);

        // Keep header/adjustment/status consistent
        await _reconciler.ReconcileAsync(receiptId, ct);

        return item.ToDto();
    }

    public async Task<ReceiptItemDto?> UpdateItemAsync(Guid receiptId, Guid itemId, UpdateReceiptItemDto dto, CancellationToken ct = default)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        var item = await _db.ReceiptItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReceiptId == receiptId, ct);
        if (item is null) return null;

        // optimistic concurrency
        _db.Entry(item).Property(x => x.Version).OriginalValue = dto.Version;

        // Apply incoming changes
        item.ApplyUpdate(dto);

        // Normalize strings
        item.Label = (item.Label ?? string.Empty).Trim();
        item.Unit = string.IsNullOrWhiteSpace(item.Unit) ? null : item.Unit.Trim();
        item.Category = string.IsNullOrWhiteSpace(item.Category) ? null : item.Category.Trim();
        item.Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim();

        // Position: if API allows changing it, keep existing value if missing
        if (dto.Position.HasValue) item.Position = dto.Position.Value;

        // Numeric guards
        if (item.Qty <= 0m) throw new ArgumentException("Qty must be > 0.");
        if (item.UnitPrice < 0m) throw new ArgumentException("UnitPrice cannot be negative.");
        if (item.Discount is < 0m) throw new ArgumentException("Discount cannot be negative.");
        if (item.Tax is < 0m) throw new ArgumentException("Tax cannot be negative.");

        // Normalize numbers
        item.Qty = NormalizeQty(item.Qty);
        item.UnitPrice = Round2(item.UnitPrice);
        item.Discount = item.Discount is null ? null : Round2(item.Discount.Value);
        item.Tax = item.Tax is null ? null : Round2(item.Tax.Value);
        item.UpdatedAt = _clock.UtcNow;

        // Normalize label by removing qty tokens that match the (now-normalized) qty
        item.Label = NormalizeLabelByQty(item.Label, item.Qty);

        // Block manual edits to system "Adjustment" or attempts to rename to "Adjustment"
        if (item.IsSystemGenerated && string.Equals(item.Label, "Adjustment", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("System-generated Adjustment cannot be modified manually.");
        if (!item.IsSystemGenerated && string.Equals(item.Label, "Adjustment", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The 'Adjustment' line is system-managed and cannot be modified manually.");

        // Block totals/promo/meta labels on updates too
        if (!item.IsSystemGenerated && LooksLikeNonItem(item.Label))
            throw new InvalidOperationException("Labels like Subtotal/Tax/Tip/Discount/Promo are totals/meta and cannot be used for items.");

        // Clamp discount ≤ line subtotal
        var maxDiscount = Round2(item.Qty * item.UnitPrice);
        if (item.Discount is > 0m && item.Discount > maxDiscount) item.Discount = maxDiscount;

        ReceiptItemMaps.Recalculate(item);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Concurrency conflict. Reload the item and try again.");
        }

        await _reconciler.ReconcileAsync(receiptId, ct);
        return item.ToDto();
    }

    public async Task<bool> DeleteItemAsync(Guid receiptId, Guid itemId, uint? version, CancellationToken ct = default)
    {
        var item = await _db.ReceiptItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReceiptId == receiptId, ct);
        if (item is null) return false;

        // Block deletes of system "Adjustment"
        if (item.IsSystemGenerated && string.Equals(item.Label, "Adjustment", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("System-generated Adjustment cannot be deleted manually.");

        if (version.HasValue)
            _db.Entry(item).Property(x => x.Version).OriginalValue = version.Value;

        _db.ReceiptItems.Remove(item);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Concurrency conflict while deleting the item.");
        }

        await _reconciler.ReconcileAsync(receiptId, ct);
        return true;
    }

    #region Helpers
    private static decimal NormalizeQty(decimal qty) =>
        qty <= 0m ? 1m : decimal.Round(qty, 3, MidpointRounding.AwayFromZero);

    private static decimal Round2(decimal v) =>
        decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    // Remove leading or trailing "qty×" tokens that match the numeric qty.
    // Examples:
    //  "2x Bagel" (qty=2)     -> "Bagel"
    //  "Bagel x2" (qty=2)     -> "Bagel"
    //  "Coffee 1x" (qty=1)    -> "Coffee"
    //  "Muffin ×2" (qty=2)    -> "Muffin"
    private static readonly Regex QtyPrefix = new(@"^\s*(\d{1,3})\s*[x×]\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex QtySuffixXn = new(@"\s*[x×]\s*(\d{1,3})\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex QtySuffixNx = new(@"\s*(\d{1,3})\s*[x×]\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string NormalizeLabelByQty(string label, decimal qty)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;

        var t = label.Trim();
        var q = (int)decimal.Round(qty <= 0 ? 1 : qty, 0, MidpointRounding.AwayFromZero);

        // strip matching prefix "2x "
        var mPref = QtyPrefix.Match(t);
        if (mPref.Success && int.TryParse(mPref.Groups[1].Value, out var qp) && qp == q)
            t = QtyPrefix.Replace(t, "");

        // strip matching suffix " x2"
        var mSufXn = QtySuffixXn.Match(t);
        if (mSufXn.Success && int.TryParse(mSufXn.Groups[1].Value, out var qx) && qx == q)
            t = QtySuffixXn.Replace(t, "");

        // strip matching suffix " 2x"
        var mSufNx = QtySuffixNx.Match(t);
        if (mSufNx.Success && int.TryParse(mSufNx.Groups[1].Value, out var qn) && qn == q)
            t = QtySuffixNx.Replace(t, "");

        return t.Trim();
    }

    // Keep server source of truth consistent with orchestrator: block totals/promo/meta as items
    private static readonly Regex NonItemPhrase = new(
        @"\b(subtotal|sub\s*total|total(?!\s*wine)|amount\s*due|sales?\s*tax|tax|tip|gratuity|service(\s*fee)?|discount|promo|promotion|coupon|offer|save|spend|member|loyalty|rewards|bogo|%[\s-]*off|pre[-\s]?discount\s*subtotal|discount\s*total)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TrailingMinusOrParens = new(
        @"\(\s*\d+(?:[.,]\d+)?\s*\)|\d+(?:[.,]\d+)?\s*[-–—]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool LooksLikeNonItem(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return true;
        var d = desc.ToLowerInvariant();
        if (NonItemPhrase.IsMatch(d)) return true;
        if (TrailingMinusOrParens.IsMatch(d)) return true; // e.g., "(5.16)" or "5.16-"
        return false;
    }
    #endregion
}
