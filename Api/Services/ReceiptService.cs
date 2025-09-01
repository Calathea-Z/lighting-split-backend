// Api/Services/ReceiptService.cs
using Api.Data;
using Api.Dtos;
using Api.Interfaces;
using Api.Mappers;
using Api.Models;
using Api.Options;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

public sealed class ReceiptService(
    LightningDbContext db,
    BlobServiceClient blobSvc,
    IParseQueue parseQueue,
    IOptions<StorageOptions> storageOptions
) : IReceiptService
{
    private readonly StorageOptions _storage = storageOptions.Value;
    // ---------------------------
    // Create from DTO (server-side math + sane defaults)
    // ---------------------------
    public async Task<ReceiptSummaryDto> CreateAsync(CreateReceiptDto dto, CancellationToken ct = default)
    {
        if (dto is null) throw new ArgumentException("Body is required.", nameof(dto));
        if (dto.Items is null || dto.Items.Count == 0)
            throw new ArgumentException("At least one item is required.", nameof(dto.Items));
        if (dto.Items.Any(i => i.Qty <= 0 || i.UnitPrice < 0))
            throw new ArgumentException("Item quantities must be > 0 and prices must be >= 0.", nameof(dto.Items));

        // If you already have dto.ToEntity(), keep it; then normalize:
        var entity = dto.ToEntity(); // assumes you map CreateReceiptItemDto -> ReceiptItem etc.
        entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
        entity.CreatedAt = entity.CreatedAt == default ? DateTimeOffset.UtcNow : entity.CreatedAt;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.Status = string.IsNullOrWhiteSpace(entity.Status) ? "Parsed" : entity.Status;

        // Recalculate all line items (line math & timestamps)
        foreach (var i in entity.Items)
        {
            ReceiptMappers.Recalculate(i);
            i.CreatedAt = i.CreatedAt == default ? DateTimeOffset.UtcNow : i.CreatedAt;
            i.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // If totals were omitted, roll up from items
        if (entity.SubTotal is null || entity.Total is null)
        {
            var sub = entity.Items.Sum(x => x.LineSubtotal);
            var tax = entity.Items.Sum(x => x.Tax ?? 0m);
            var tot = entity.Items.Sum(x => x.LineTotal);
            entity.SubTotal ??= (sub == 0m ? null : sub);
            entity.Tax ??= (tax == 0m ? null : tax);
            entity.Total ??= (tot == 0m ? null : tot);
        }

        db.Receipts.Add(entity);
        await db.SaveChangesAsync(ct);

        return entity.ToSummaryDto();
    }

    // ---------------------------
    // Read (detail)
    // ---------------------------
    public async Task<ReceiptDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.Receipts
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return r?.ToDetailDto();
    }

    // ---------------------------
    // List (summary projection; no Include needed)
    // ---------------------------
    public async Task<IReadOnlyList<ReceiptSummaryDto>> ListAsync(string? ownerUserId, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        var q = db.Receipts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(ownerUserId))
            q = q.Where(r => r.OwnerUserId == ownerUserId);

        return await q
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReceiptSummaryDto(
                r.Id,
                r.Status,
                r.SubTotal,
                r.Tax,
                r.Tip,
                r.Total,
                r.CreatedAt,
                r.UpdatedAt,
                r.Items.Count
            ))
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }


    // ---------------------------
    // Delete (best-effort blob cleanup)
    // ---------------------------
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.Receipts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;

        db.Receipts.Remove(r);
        await db.SaveChangesAsync(ct);

        // Try to delete blob if we know where it is (ignore failures)
        if (!string.IsNullOrWhiteSpace(r.BlobContainer) && !string.IsNullOrWhiteSpace(r.BlobName))
        {
            try
            {
                var container = blobSvc.GetBlobContainerClient(r.BlobContainer);
                var blob = container.GetBlobClient(r.BlobName);
                await blob.DeleteIfExistsAsync(cancellationToken: ct);
            }
            catch { /* swallow */ }
        }

        return true;
    }

    // ---------------------------
    // Update totals (idempotent; uses line rollups as fallback)
    // ---------------------------
    public async Task<ReceiptSummaryDto?> UpdateTotalsAsync(Guid id, UpdateTotalsDto dto, CancellationToken ct = default)
    {
        var r = await db.Receipts.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;

        // Use persisted line math when available
        var lineSub = r.Items.Sum(i => i.LineSubtotal);
        var lineTax = r.Items.Sum(i => i.Tax ?? 0m);
        var lineTot = r.Items.Sum(i => i.LineTotal);

        r.SubTotal = dto.SubTotal ?? r.SubTotal ?? (lineSub == 0m ? null : lineSub);
        r.Tax = dto.Tax ?? r.Tax ?? (lineTax == 0m ? null : lineTax);
        r.Tip = dto.Tip ?? r.Tip;
        r.Total = dto.Total ?? r.Total ?? ((r.SubTotal ?? 0m) + (r.Tax ?? 0m) + (r.Tip ?? 0m));

        // Advance state on success; reset error
        r.Status = "Parsed";
        r.ParseError = null;
        r.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return r.ToSummaryDto();
    }

    // ---------------------------
    // Upload (blob fields + metadata + enqueue)
    // ---------------------------
    public async Task<ReceiptSummaryDto> UploadAsync(UploadReceiptItemDto dto, CancellationToken ct = default)
    {
        var file = dto.File;
        if (file is null || file.Length == 0)
            throw new ArgumentException("File is required.", nameof(dto.File));
        if (file.Length > 20_000_000)
            throw new ArgumentException("File too large (>20MB).");

        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            OwnerUserId = null, // populate when auth is wired
            Status = "PendingParse",
            Items = []
        };

        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(ct);

        var containerName = _storage.ReceiptsContainer ?? "receipts";
        var container = blobSvc.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";
        var blobName = $"{receipt.Id:N}/{Path.GetRandomFileName()}{ext}";
        var blob = container.GetBlobClient(blobName);

        var headers = new Azure.Storage.Blobs.Models.BlobHttpHeaders
        {
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType
        };

        var metadata = new Dictionary<string, string>
        {
            ["receiptId"] = receipt.Id.ToString(),
            ["uploadedAt"] = DateTimeOffset.UtcNow.ToString("o"),
            ["storeName"] = dto.StoreName ?? string.Empty,
            ["purchasedAt"] = dto.PurchasedAt?.ToString("o") ?? string.Empty,
            ["notes"] = dto.Notes ?? string.Empty
        };

        await using (var s = file.OpenReadStream())
        {
            if (_storage.OverwriteOnUpload)
                await blob.DeleteIfExistsAsync(cancellationToken: ct);

            await blob.UploadAsync(
                s,
                new Azure.Storage.Blobs.Models.BlobUploadOptions
                {
                    HttpHeaders = headers,
                    Metadata = metadata
                },
                ct
            );
        }

        receipt.BlobContainer = containerName;
        receipt.BlobName = blobName;
        receipt.OriginalFileUrl = blob.Uri.ToString();
        receipt.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        await parseQueue.EnqueueAsync(new(containerName, blobName, receipt.Id.ToString()), ct);

        return receipt.ToSummaryDto();
    }

    public async Task<bool> MarkParseFailedAsync(Guid id, string error, CancellationToken ct = default)
    {
        var r = await db.Receipts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;

        // clamp to column limit (we set 2000)
        var msg = error ?? "Unknown parse error.";
        if (msg.Length > 2000) msg = msg[..2000];

        r.Status = "FailedParse";
        r.ParseError = msg;
        r.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ReceiptItemDto?> AddItemAsync(Guid receiptId, CreateReceiptItemDto dto, CancellationToken ct = default)
{
    var r = await db.Receipts.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == receiptId, ct);
    if (r is null) return null;

    var i = dto.ToEntity(receiptId);
    db.ReceiptItems.Add(i);

    // optional rollup if header totals are missing
    r.UpdatedAt = DateTimeOffset.UtcNow;
    var (sub, tax, tot) = r.Rollup();
    r.SubTotal ??= sub;
    r.Tax      ??= tax;
    r.Total    ??= tot;

    await db.SaveChangesAsync(ct);
    return i.ToDto();
}

public async Task<ReceiptItemDto?> UpdateItemAsync(Guid receiptId, Guid itemId, UpdateReceiptItemDto dto, CancellationToken ct = default)
{
    var i = await db.ReceiptItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReceiptId == receiptId, ct);
    if (i is null) return null;

    // Enforce optimistic concurrency using the client's Version (xmin)
    db.Entry(i).Property(x => x.Version).OriginalValue = dto.Version;

    i.ApplyUpdate(dto);

    // Touch parent receipt UpdatedAt; optionally update header if totals are missing
    var r = await db.Receipts.FirstAsync(x => x.Id == receiptId, ct);
    r.UpdatedAt = DateTimeOffset.UtcNow;
    if (r.SubTotal is null || r.Total is null || r.Tax is null)
    {
        var items = await db.ReceiptItems.Where(x => x.ReceiptId == receiptId).ToListAsync(ct);
        r.SubTotal ??= items.Sum(x => x.LineSubtotal);
        var taxSum = items.Sum(x => x.Tax ?? 0m);
        r.Tax ??= (taxSum == 0m ? null : taxSum);
        r.Total ??= items.Sum(x => x.LineTotal);
    }

    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
        // surface a clean 409 to the controller
        throw new InvalidOperationException("Concurrency conflict. Reload the item and try again.");
    }

    return i.ToDto();
}

public async Task<bool> DeleteItemAsync(Guid receiptId, Guid itemId, uint? version, CancellationToken ct = default)
{
    var i = await db.ReceiptItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReceiptId == receiptId, ct);
    if (i is null) return false;

    // If caller provided a Version, enforce it
    if (version.HasValue)
        db.Entry(i).Property(x => x.Version).OriginalValue = version.Value;

    db.ReceiptItems.Remove(i);

    // Touch parent receipt and, if header totals are derived, recompute
    var r = await db.Receipts.Include(x => x.Items).FirstAsync(x => x.Id == receiptId, ct);
    r.UpdatedAt = DateTimeOffset.UtcNow;
    if (r.SubTotal is null || r.Total is null || r.Tax is null)
    {
        var (sub, tax, tot) = r.Rollup();
        r.SubTotal = sub;
        r.Tax      = tax;
        r.Total    = tot;
    }

    try
    {
        await db.SaveChangesAsync(ct);
        return true;
    }
    catch (DbUpdateConcurrencyException)
    {
        throw new InvalidOperationException("Concurrency conflict while deleting the item.");
    }
}

}
