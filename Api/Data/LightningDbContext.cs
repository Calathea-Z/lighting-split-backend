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
        // Receipt Entity Configuration
        // =========================
        b.Entity<Receipt>(e =>
        {
            e.HasKey(x => x.Id);

            // One-to-many relationship with ReceiptItems (cascade delete)
            e.HasMany(x => x.Items)
             .WithOne(i => i.Receipt)
             .HasForeignKey(i => i.ReceiptId)
             .OnDelete(DeleteBehavior.Cascade);

            // Performance indexes for common query patterns
            e.HasIndex(x => x.CreatedAt);                    // Time-based queries
            e.HasIndex(x => x.OwnerUserId);                  // User-specific queries
            e.HasIndex(x => x.Status);                       // Status filtering
            e.HasIndex(x => new { x.Status, x.CreatedAt });  // Status + time queries
            e.HasIndex(x => x.NeedsReview);                  // Review queue queries

            // String column length constraints
            e.Property(x => x.OriginalFileUrl).HasMaxLength(500);
            e.Property(x => x.RawText).HasMaxLength(100_000);
            e.Property(x => x.BlobContainer).HasMaxLength(100);
            e.Property(x => x.BlobName).HasMaxLength(500);
            e.Property(x => x.ParseError).HasMaxLength(2000);

            // Monetary precision (12 digits total, 2 decimal places)
            e.Property(x => x.SubTotal).HasColumnType("numeric(12,2)");
            e.Property(x => x.Tax).HasColumnType("numeric(12,2)");
            e.Property(x => x.Tip).HasColumnType("numeric(12,2)");
            e.Property(x => x.Total).HasColumnType("numeric(12,2)");

            // Status field with length constraint
            e.Property(x => x.Status)
             .HasMaxLength(32)
             .IsRequired();

            // Audit timestamps with PostgreSQL timezone support
            e.Property(x => x.CreatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            e.Property(x => x.UpdatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            // Optimistic concurrency control using PostgreSQL xmin
            e.Property(x => x.Version).IsRowVersion();

            // Reconciliation transparency fields for financial accuracy
            e.Property(x => x.ComputedItemsSubtotal).HasColumnType("numeric(12,2)");
            e.Property(x => x.BaselineSubtotal).HasColumnType("numeric(12,2)");
            e.Property(x => x.Discrepancy).HasColumnType("numeric(12,2)");
            e.Property(x => x.Reason).HasMaxLength(2000);

            // Review flag with default value
            e.Property(x => x.NeedsReview).HasDefaultValue(false);
        });

        // =========================
        // ReceiptItem Entity Configuration
        // =========================
        b.Entity<ReceiptItem>(e =>
        {
            e.HasKey(x => x.Id);

            // Many-to-one relationship with Receipt (cascade delete)
            e.HasOne(x => x.Receipt)
             .WithMany(r => r.Items)
             .HasForeignKey(x => x.ReceiptId)
             .OnDelete(DeleteBehavior.Cascade);

            // Descriptive text fields with length constraints
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(16);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(1000);

            // Composite index for ordering items within receipts
            e.HasIndex(x => new { x.ReceiptId, x.Position });

            // Quantity and monetary precision
            e.Property(x => x.Qty).HasColumnType("numeric(9,3)");        // Up to 999,999.999
            e.Property(x => x.UnitPrice).HasColumnType("numeric(12,2)"); // Up to 9,999,999,999.99
            e.Property(x => x.Discount).HasColumnType("numeric(12,2)");
            e.Property(x => x.Tax).HasColumnType("numeric(12,2)");
            e.Property(x => x.LineSubtotal).HasColumnType("numeric(12,2)");
            e.Property(x => x.LineTotal).HasColumnType("numeric(12,2)");

            // System-generated item flag (for auto-calculated items)
            e.Property(x => x.IsSystemGenerated)
             .HasDefaultValue(false);

            // Audit timestamps with PostgreSQL timezone support
            e.Property(x => x.CreatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            e.Property(x => x.UpdatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            // Optimistic concurrency control using PostgreSQL xmin
            e.Property(x => x.Version).IsRowVersion();
        });
    }
}
