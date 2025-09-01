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
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.Status, x.CreatedAt });

            // Column sizes
            e.Property(x => x.OriginalFileUrl).HasMaxLength(500);
            e.Property(x => x.RawText).HasMaxLength(100_000);
            e.Property(x => x.BlobContainer).HasMaxLength(100);
            e.Property(x => x.BlobName).HasMaxLength(500);
            e.Property(x => x.ParseError).HasMaxLength(2000);

            // Money precision
            e.Property(x => x.SubTotal).HasColumnType("numeric(12,2)");
            e.Property(x => x.Tax).HasColumnType("numeric(12,2)");
            e.Property(x => x.Tip).HasColumnType("numeric(12,2)");
            e.Property(x => x.Total).HasColumnType("numeric(12,2)");

            // Status string
            e.Property(x => x.Status)
             .HasMaxLength(32)
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

            // Map Version => xmin (provider does not create a physical column)
            e.Property(x => x.Version).IsRowVersion();
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

            // Descriptive columns
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(16);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.Notes).HasMaxLength(1000);

            // Ordering within a receipt
            e.HasIndex(x => new { x.ReceiptId, x.Position });

            // Quantities & money precision
            e.Property(x => x.Qty).HasColumnType("numeric(9,3)");
            e.Property(x => x.UnitPrice).HasColumnType("numeric(12,2)");
            e.Property(x => x.Discount).HasColumnType("numeric(12,2)");
            e.Property(x => x.Tax).HasColumnType("numeric(12,2)");
            e.Property(x => x.LineSubtotal).HasColumnType("numeric(12,2)");
            e.Property(x => x.LineTotal).HasColumnType("numeric(12,2)");

            // Timestamps
            e.Property(x => x.CreatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            e.Property(x => x.UpdatedAt)
             .HasColumnType("timestamptz")
             .HasDefaultValueSql("now()")
             .IsRequired();

            // Map Version => xmin (no physical column)
            e.Property(x => x.Version).IsRowVersion();
        });
    }
}
