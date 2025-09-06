using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class LightningDbContext(DbContextOptions<LightningDbContext> options) : DbContext(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // =========================
        // Receipt
        // =========================
        b.Entity<Receipt>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasMany(x => x.Items)
             .WithOne(i => i.Receipt)
             .HasForeignKey(i => i.ReceiptId)
             .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => x.NeedsReview);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.OwnerUserId, x.CreatedAt });

            // Idempotency (unique per owner; nullable allowed)
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.HasIndex(x => new { x.OwnerUserId, x.IdempotencyKey })
             .IsUnique()
             .HasFilter("\"IdempotencyKey\" IS NOT NULL"); // Postgres

            // Fast blob lookups (non-unique; helps dedupe/debug)
            e.HasIndex(x => new { x.BlobContainer, x.BlobName });

            // Strings
            e.Property(x => x.OriginalFileUrl).HasMaxLength(2048);
            e.Property(x => x.BlobContainer).HasMaxLength(128);
            e.Property(x => x.BlobName).HasMaxLength(256);
            e.Property(x => x.ParseError).HasMaxLength(512);
            e.Property(x => x.Reason).HasMaxLength(256);

            // Large text
            e.Property(x => x.RawText).HasColumnType("text");

            // Money precision (matches model [Precision(18,2)])
            e.Property(x => x.SubTotal).HasPrecision(18, 2);
            e.Property(x => x.Tax).HasPrecision(18, 2);
            e.Property(x => x.Tip).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);

            e.Property(x => x.ComputedItemsSubtotal).HasPrecision(18, 2);
            e.Property(x => x.BaselineSubtotal).HasPrecision(18, 2);
            e.Property(x => x.Discrepancy).HasPrecision(18, 2);

            // Enum as string
            e.Property(x => x.Status)
             .HasConversion<string>()
             .HasMaxLength(64)
             .IsRequired();

            // Timestamps
            e.Property(x => x.CreatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            e.Property(x => x.UpdatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            // Concurrency (Postgres xmin)
            e.Property(x => x.Version).IsRowVersion();

            // Flags
            e.Property(x => x.NeedsReview).HasDefaultValue(false);
        });


        // =========================
        // ReceiptItem
        // =========================
        b.Entity<ReceiptItem>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Receipt)
             .WithMany(r => r.Items)
             .HasForeignKey(x => x.ReceiptId)
             .OnDelete(DeleteBehavior.Cascade);

            // Descriptive
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(16);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(1000); 

            // Ordering + lookup
            e.HasIndex(x => new { x.ReceiptId, x.Position });

            // Quantities & money (match model [Precision])
            e.Property(x => x.Qty).HasPrecision(9, 3);
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.Property(x => x.Discount).HasPrecision(12, 2);
            e.Property(x => x.Tax).HasPrecision(12, 2);
            e.Property(x => x.LineSubtotal).HasPrecision(12, 2);
            e.Property(x => x.LineTotal).HasPrecision(12, 2);

            // Flags
            e.Property(x => x.IsSystemGenerated).HasDefaultValue(false);

            // Timestamps
            e.Property(x => x.CreatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            e.Property(x => x.UpdatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            // Concurrency (Postgres xmin)
            e.Property(x => x.Version).IsRowVersion();
        });
    }
}
