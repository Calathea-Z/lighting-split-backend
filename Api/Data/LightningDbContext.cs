using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class LightningDbContext(DbContextOptions<LightningDbContext> options) : DbContext(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Receipt>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasMany(x => x.Items)
             .WithOne(i => i.Receipt)
             .HasForeignKey(i => i.ReceiptId)
             .OnDelete(DeleteBehavior.Cascade);

            // Useful indexes
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.OwnerUserId);

            // Column sizes
            e.Property(x => x.OriginalFileUrl).HasMaxLength(500);
            e.Property(x => x.RawText).HasMaxLength(100_000);

            // Money precision
            e.Property(x => x.SubTotal).HasColumnType("numeric(12,2)");
            e.Property(x => x.Tax).HasColumnType("numeric(12,2)");
            e.Property(x => x.Tip).HasColumnType("numeric(12,2)");
            e.Property(x => x.Total).HasColumnType("numeric(12,2)");
        });

        b.Entity<ReceiptItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasColumnType("numeric(12,2)");
        });
    }
}