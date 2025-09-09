using Api.Models;
using Api.Models.Owners;
using Api.Models.Receipts;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class LightningDbContext(DbContextOptions<LightningDbContext> options) : DbContext(options)
{
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();

    public DbSet<Owner> Owners => Set<Owner>();
    public DbSet<OwnerPayoutMethod> OwnerPayoutMethods => Set<OwnerPayoutMethod>();
    public DbSet<PayoutPlatform> PayoutPlatforms => Set<PayoutPlatform>();

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

            // Money precision
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

            // ===== Parse meta =====
            e.Property(x => x.ParsedBy)
             .HasConversion<string>()       // store enum as text
             .HasMaxLength(32);

            e.Property(x => x.LlmModel).HasMaxLength(128);
            e.Property(x => x.ParserVersion).HasMaxLength(64);
            e.Property(x => x.RejectReason).HasMaxLength(512);

            e.Property(x => x.LlmAttempted).HasDefaultValue(false);
            e.Property(x => x.LlmAccepted).HasDefaultValue(null);

            e.Property(x => x.ParsedAt).HasColumnType("timestamptz");

            e.HasIndex(x => x.ParsedAt);
            e.HasIndex(x => new { x.ParsedBy, x.ParsedAt });

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

            // Quantities & money
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

        // =========================
        // Owner
        // =========================
        b.Entity<Owner>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.KeyHash).IsRequired().HasMaxLength(100);
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.LastSeenAt).HasDefaultValueSql("now()");
        });

        // =========================
        // Owner Payout Method
        // =========================
        b.Entity<OwnerPayoutMethod>(e =>
        {
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Owner)
                .WithMany(o => o.Methods)
                .HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // Link to lookup table
            e.HasOne(x => x.Platform)
                .WithMany()
                .HasForeignKey(x => x.PlatformId)
                .OnDelete(DeleteBehavior.Restrict);

            e.Property(x => x.HandleOrUrl).IsRequired().HasMaxLength(256);
            e.Property(x => x.DisplayLabel).HasMaxLength(64);
            e.Property(x => x.QrImageBlobPath).HasMaxLength(256);

            // One default per owner (partial unique index)
            e.HasIndex(x => new { x.OwnerId, x.IsDefault })
             .IsUnique()
             .HasFilter("\"IsDefault\" = TRUE");

            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
        });

        // =========================
        // Payout Platform (lookup)
        // =========================
        b.Entity<PayoutPlatform>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).IsRequired().HasMaxLength(32);
            e.HasIndex(x => x.Key).IsUnique();
            e.Property(x => x.DisplayName).IsRequired().HasMaxLength(64);
            e.Property(x => x.LinkTemplate).HasMaxLength(512);
        });

        // Seed common platforms
        b.Entity<PayoutPlatform>().HasData(
            new PayoutPlatform
            {
                Id = 1,
                Key = "venmo",
                DisplayName = "Venmo",
                LinkTemplate = "https://account.venmo.com/pay?txn=pay&recipients={handle}&amount={amount}&note={note}",
                SupportsAmount = true,
                SupportsNote = true,
                HandlePattern = "^[A-Za-z0-9_.]+$",
                PrefixToStrip = "@",
                SortOrder = 10
            },
            new PayoutPlatform
            {
                Id = 2,
                Key = "cashapp",
                DisplayName = "Cash App",
                LinkTemplate = "https://cash.app/${handle}?amount={amount}&note={note}",
                SupportsAmount = true,
                SupportsNote = true,
                HandlePattern = "^[A-Za-z0-9_]+$",
                PrefixToStrip = "$",
                SortOrder = 20
            },
            new PayoutPlatform
            {
                Id = 3,
                Key = "paypalme",
                DisplayName = "PayPal.Me",
                LinkTemplate = "https://paypal.me/{handle}/{amount}",
                SupportsAmount = true,
                SupportsNote = false,
                HandlePattern = "^[A-Za-z0-9.]+$",
                PrefixToStrip = "paypal.me/",
                SortOrder = 30
            },
            new PayoutPlatform
            {
                Id = 4,
                Key = "zelle",
                DisplayName = "Zelle",
                LinkTemplate = null,
                SupportsAmount = false,
                SupportsNote = false,
                HandlePattern = @"(^[^@\s]+@[^@\s]+\.[^@\s]+$)|(^\+?1?\d{10}$)",
                IsInstructionsOnly = true,
                SortOrder = 40
            },
            new PayoutPlatform
            {
                Id = 5,
                Key = "applecash",
                DisplayName = "Apple Cash",
                LinkTemplate = null,
                SupportsAmount = false,
                SupportsNote = false,
                HandlePattern = @".{1,256}",
                IsInstructionsOnly = true,
                SortOrder = 50
            },
            new PayoutPlatform
            {
                Id = 6,
                Key = "custom",
                DisplayName = "Custom URL",
                LinkTemplate = "{handle}",
                SupportsAmount = false,
                SupportsNote = false,
                HandlePattern = @"^https://",
                SortOrder = 60
            }
        );
    }
}
