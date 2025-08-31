// Api/Services/ReceiptService.cs
using Api.Data;
using Api.Dtos;
using Api.Enums;
using Api.Interfaces;
using Api.Mapping;
using Api.Models;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;

public sealed class ReceiptService(
    LightningDbContext db,
    BlobServiceClient blobSvc,
    IParseQueue parseQueue
) : IReceiptService
{
    public async Task<ReceiptSummaryDto> CreateAsync(CreateReceiptDto dto, CancellationToken ct = default)
    {
        if (dto is null) throw new ArgumentException("Body is required.", nameof(dto));
        if (dto.Items is null || dto.Items.Count == 0)
            throw new ArgumentException("At least one item is required.", nameof(dto.Items));
        if (dto.Items.Any(i => i.Qty <= 0 || i.UnitPrice < 0))
            throw new ArgumentException("Item quantities must be > 0 and prices must be >= 0.", nameof(dto.Items));

        var entity = dto.ToEntity();
        entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
        entity.CreatedAt = entity.CreatedAt == default ? DateTimeOffset.UtcNow : entity.CreatedAt;
        entity.Status = entity.Status == 0 ? ReceiptStatus.Parsed : entity.Status; // or PendingParse if you prefer

        db.Receipts.Add(entity);
        await db.SaveChangesAsync(ct);

        return entity.ToSummaryDto();
    }

    public async Task<ReceiptDetailDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var receipt = await db.Receipts
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return receipt?.ToDetailDto();
    }

    public async Task<IReadOnlyList<ReceiptSummaryDto>> ListAsync(string? ownerUserId, int skip, int take, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 200);

        IQueryable<Receipt> query = db.Receipts.AsNoTracking().Include(r => r.Items)
            .OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrWhiteSpace(ownerUserId))
            query = query.Where(r => r.OwnerUserId == ownerUserId)
                         .OrderByDescending(r => r.CreatedAt);

        return await query.Skip(skip).Take(take)
            .Select(r => r.ToSummaryDto())
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.Receipts.FindAsync([id], ct);
        if (entity is null) return false;

        db.Receipts.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ReceiptSummaryDto?> UpdateTotalsAsync(Guid id, UpdateTotalsDto dto, CancellationToken ct = default)
    {
        var entity = await db.Receipts.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return null;

        // compute fallback subtotal if not provided
        var computedSub = entity.Items.Sum(i => i.UnitPrice * i.Qty);

        entity.SubTotal = dto.SubTotal ?? entity.SubTotal ?? computedSub;
        entity.Tax = dto.Tax ?? entity.Tax;
        entity.Tip = dto.Tip ?? entity.Tip;

        // if no Total provided, compute from available pieces
        entity.Total = dto.Total
            ?? entity.Total
            ?? (entity.SubTotal ?? 0m) + (entity.Tax ?? 0m) + (entity.Tip ?? 0m);

        await db.SaveChangesAsync(ct);
        return entity.ToSummaryDto();
    }

    // === Upload flow moved here earlier ===
    public async Task<ReceiptSummaryDto> UploadAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) throw new ArgumentException("File is required.", nameof(file));

        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            OwnerUserId = null,
            Status = ReceiptStatus.PendingParse,
            OriginalFileUrl = "",
            SubTotal = 0m,
            Tax = 0m,
            Tip = 0m,
            Total = 0m,
            Items = []
        };

        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(ct);

        // upload to blob
        var container = blobSvc.GetBlobContainerClient("receipts");
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var ext = Path.GetExtension(file.FileName);
        var blobName = $"{receipt.Id}/{Path.GetRandomFileName()}{ext}";
        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(file.OpenReadStream(), overwrite: true, cancellationToken: ct);

        // persist URL
        receipt.OriginalFileUrl = blob.Uri.ToString();
        await db.SaveChangesAsync(ct);

        // enqueue parse job
        await parseQueue.EnqueueAsync(new(receipt.Id, blobName), ct);

        return receipt.ToSummaryDto();
    }
}
