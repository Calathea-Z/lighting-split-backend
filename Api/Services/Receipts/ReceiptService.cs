using Api.Abstractions.Receipts;
using Api.Common.Interfaces;
using Api.Data;
using Api.Dtos.Receipts.Requests;
using Api.Dtos.Receipts.Responses;
using Api.Dtos.Receipts.Responses.Items;
using Api.Infrastructure.Interfaces;
using Api.Mappers;
using Api.Models;
using Api.Options;
using Api.Services.Receipts.Abstractions;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Services.Receipts;

public sealed class ReceiptService(
    LightningDbContext db,
    BlobServiceClient blobSvc,
    IParseQueue parseQueue,
    IOptions<StorageOptions> storageOptions,
    IClock clock,
    IReceiptReconciliationOrchestrator reconciler
) : IReceiptService
{
    private readonly StorageOptions _storage = storageOptions.Value;

    public async Task<ReceiptSummaryDto> CreateAsync(CreateReceiptDto dto, CancellationToken ct = default)
    {
        if (dto is null) throw new ArgumentException("Body is required.", nameof(dto));
        if (dto.Items is null || dto.Items.Count == 0)
            throw new ArgumentException("At least one item is required.", nameof(dto.Items));
        if (dto.Items.Any(i => i.Qty <= 0 || i.UnitPrice < 0))
            throw new ArgumentException("Item quantities must be > 0 and prices must be >= 0.", nameof(dto.Items));

        var entity = dto.ToEntity();

        // Id / timestamps (guard if mapper already sets them)
        entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
        entity.CreatedAt = entity.CreatedAt == default ? clock.UtcNow : entity.CreatedAt;
        entity.UpdatedAt = clock.UtcNow;

        // ENUM default instead of string
        entity.Status = entity.Status == default ? ReceiptStatus.Parsed : entity.Status;

        foreach (var i in entity.Items)
        {
            ReceiptItemMaps.Recalculate(i);
            i.CreatedAt = i.CreatedAt == default ? clock.UtcNow : i.CreatedAt;
            i.UpdatedAt = clock.UtcNow;
        }

        // Apply header totals if provided, else keep mapper values
        entity.SubTotal = dto.SubTotal ?? entity.SubTotal;
        entity.Tax = dto.Tax ?? entity.Tax;
        entity.Tip = dto.Tip ?? entity.Tip;
        entity.Total = dto.Total ?? entity.Total;

        // If totals omitted, roll up from items (rounded)
        if (entity.SubTotal is null || entity.Total is null)
        {
            var sub = Money.Round2(entity.Items.Sum(x => x.LineSubtotal));
            var tax = Money.Round2(entity.Items.Sum(x => x.Tax ?? 0m));
            var tot = Money.Round2(entity.Items.Sum(x => x.LineTotal));

            entity.SubTotal ??= (sub == 0m ? null : sub);
            entity.Tax ??= (tax == 0m ? null : tax);
            entity.Total ??= (tot == 0m ? null : tot);
        }

        db.Receipts.Add(entity);
        await db.SaveChangesAsync(ct);

        // Centralized reconciliation (sets transparency fields + may adjust status)
        await reconciler.ReconcileAsync(entity.Id, ct);

        return entity.ToSummaryDto();
    }

    public async Task<ReceiptDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.Receipts
            .AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return r?.ToDetailDto();
    }

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
                r.Items.Count,
                r.ComputedItemsSubtotal,
                r.BaselineSubtotal,
                r.Discrepancy,
                r.Reason,
                r.NeedsReview
            ))
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.Receipts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;

        db.Receipts.Remove(r);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(r.BlobContainer) && !string.IsNullOrWhiteSpace(r.BlobName))
        {
            try
            {
                var container = blobSvc.GetBlobContainerClient(r.BlobContainer);
                var blob = container.GetBlobClient(r.BlobName);
                await blob.DeleteIfExistsAsync(cancellationToken: ct);
            }
            catch { /* best-effort cleanup */ }
        }
        return true;
    }

    public async Task<ReceiptSummaryDto?> UpdateTotalsAsync(Guid id, UpdateTotalsDto dto, CancellationToken ct = default)
    {
        var exists = await db.Receipts.AnyAsync(x => x.Id == id, ct);
        if (!exists) return null;

        var agg = await db.ReceiptItems
            .Where(i => i.ReceiptId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Sub = g.Sum(x => x.LineSubtotal),
                Tax = g.Sum(x => x.Tax ?? 0m),
                Tot = g.Sum(x => x.LineTotal)
            })
            .SingleOrDefaultAsync(ct);

        // Prefer DTO > existing > rollup
        var current = await db.Receipts.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.SubTotal, r.Tax, r.Tip, r.Total })
            .FirstAsync(ct);

        decimal? sub = dto.SubTotal ?? current.SubTotal ?? (agg == null ? null : (decimal?)agg.Sub);
        decimal? tax = dto.Tax ?? current.Tax ?? (agg == null || agg.Tax == 0m ? null : (decimal?)agg.Tax);
        decimal? tip = dto.Tip ?? current.Tip;
        decimal? tot = dto.Total ?? current.Total ?? ((sub ?? 0m) + (tax ?? 0m) + (tip ?? 0m));

        // Round to 2dp consistently to avoid epsilon mismatches
        sub = sub is null ? null : Money.Round2(sub.Value);
        tax = tax is null ? null : Money.Round2(tax.Value);
        tip = tip is null ? null : Money.Round2(tip.Value);
        tot = tot is null ? null : Money.Round2(tot.Value);

        await db.Receipts
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SubTotal, _ => sub)
                .SetProperty(r => r.Tax, _ => tax)
                .SetProperty(r => r.Tip, _ => tip)
                .SetProperty(r => r.Total, _ => tot)
                .SetProperty(r => r.ParseError, _ => (string?)null)
                .SetProperty(r => r.UpdatedAt, _ => clock.UtcNow), ct);

        // Centralized reconciliation
        await reconciler.ReconcileAsync(id, ct);

        return await db.Receipts.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(r => new ReceiptSummaryDto(
                r.Id, r.Status, r.SubTotal, r.Tax, r.Tip, r.Total,
                r.CreatedAt, r.UpdatedAt, r.Items.Count, r.ComputedItemsSubtotal,
                r.BaselineSubtotal, r.Discrepancy, r.Reason, r.NeedsReview))
            .FirstAsync(ct);
    }

    public async Task<ReceiptSummaryDto> UploadAsync(
        UploadReceiptItemDto dto,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        var file = dto.File;
        if (file is null || file.Length == 0)
            throw new ArgumentException("File is required.", nameof(dto.File));
        if (file.Length > 20_000_000)
            throw new ArgumentException("File too large (>20MB).");

        // --- Idempotency pre-read (global while OwnerUserId is null) ---
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await db.Receipts
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);

            if (existing is not null)
                return existing.ToSummaryDto();
        }

        var now = clock.UtcNow;

        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
            OwnerUserId = null, // TODO: set when auth lands
            Status = ReceiptStatus.PendingParse,
            Items = [],
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey
        };

        db.Receipts.Add(receipt);

        try
        {
            // Reserve the unique (IdempotencyKey) early to win races
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another request with the same key won; return that one
            var winner = await db.Receipts.AsNoTracking()
                .FirstAsync(r => r.IdempotencyKey == idempotencyKey, ct);

            return winner.ToSummaryDto();
        }

        // --- Blob upload ---
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
            ["uploadedAt"] = now.ToString("o"),
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
        receipt.UpdatedAt = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        // Keep "PendingParse"; parser will update later
        await parseQueue.EnqueueAsync(new(containerName, blobName, receipt.Id.ToString()), ct);

        return receipt.ToSummaryDto();
    }

    public async Task<bool> MarkParseFailedAsync(Guid id, string? error, CancellationToken ct = default)
    {
        var msg = string.IsNullOrWhiteSpace(error) ? "Unknown parse error." : error;
        if (msg.Length > 2000) msg = msg[..2000];

        var rows = await db.Receipts
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, _ => ReceiptStatus.FailedParse)
                .SetProperty(r => r.ParseError, _ => msg)
                .SetProperty(r => r.NeedsReview, _ => true)
                .SetProperty(r => r.UpdatedAt, _ => clock.UtcNow), ct);

        return rows > 0;
    }

    public async Task<ReceiptSummaryDto?> UpdateRawTextAsync(Guid id, UpdateRawTextDto dto, CancellationToken ct = default)
    {
        var trimmed = dto.RawText?.Trim();

        var rows = await db.Receipts
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RawText, _ => trimmed)
                .SetProperty(r => r.UpdatedAt, _ => clock.UtcNow), ct);

        if (rows == 0) return null;

        return await db.Receipts.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(r => r.ToSummaryDto())
            .FirstAsync(ct);
    }

    public async Task<ReceiptSummaryDto?> UpdateStatusAsync(Guid id, UpdateStatusDto dto, CancellationToken ct = default)
    {
        var rows = await db.Receipts
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, _ => dto.Status)
                .SetProperty(r => r.UpdatedAt, _ => clock.UtcNow), ct);

        if (rows == 0) return null;

        return await db.Receipts.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(r => r.ToSummaryDto())
            .FirstAsync(ct);
    }

    public async Task<ReceiptSummaryDto?> UpdateReviewAsync(Guid id, UpdateReviewDto dto, CancellationToken ct = default)
    {
        var rows = await db.Receipts
            .Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.NeedsReview, _ => dto.NeedsReview)
                .SetProperty(r => r.UpdatedAt, _ => clock.UtcNow), ct);

        if (rows == 0) return null;

        return await db.Receipts.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(r => r.ToSummaryDto())
            .FirstAsync(ct);
    }

    // Helpers
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Npgsql: SQLSTATE 23505 = unique_violation
        if (ex.InnerException is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            return true;

        // Fallback: message contains 'duplicate key'
        return ex.InnerException?.Message?.IndexOf("duplicate key", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
