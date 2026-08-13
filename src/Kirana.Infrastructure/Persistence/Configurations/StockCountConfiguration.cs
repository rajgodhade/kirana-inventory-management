using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> builder)
    {
        builder.Property(x => x.CountNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(x => x.CountNumber).IsUnique();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Notes).HasMaxLength(500);

        builder.HasIndex(x => x.StartedAtUtc);
        builder.HasIndex(x => x.Status);

        // Only one count may be open at a time, enforced by the database rather than by a
        // check-then-insert race in the service. Two overlapping counts would each snapshot the
        // same products and then apply conflicting adjustments to the same inventory rows.
        //
        // NOTE the quoting: SQLite needs the column name quoted here exactly as the sibling
        // ProductBarcodes primary index does, or the filter never matches.
        builder.HasIndex(x => x.Status)
            .IsUnique()
            .HasFilter("\"Status\" = 'InProgress'")
            .HasDatabaseName("IX_StockCounts_SingleInProgress");

        builder.HasOne(x => x.StartedByUser).WithMany()
            .HasForeignKey(x => x.StartedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CompletedByUser).WithMany()
            .HasForeignKey(x => x.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockCountItemConfiguration : IEntityTypeConfiguration<StockCountItem>
{
    public void Configure(EntityTypeBuilder<StockCountItem> builder)
    {
        builder.Property(x => x.ProductNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ProductCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(x => x.SkuSnapshot).HasMaxLength(60);
        builder.Property(x => x.BarcodeSnapshot).HasMaxLength(48);
        builder.Property(x => x.Notes).HasMaxLength(500);

        builder.Property(x => x.UnitSnapshot).HasConversion<string>().HasMaxLength(20);

        // 18,3 matches every other quantity column in the schema (PurchaseItem.Quantity,
        // HeldBillItem.Quantity), so a counted 12.5 Kg round-trips identically to a purchased one.
        builder.Property(x => x.SystemQuantity).HasPrecision(18, 3);
        builder.Property(x => x.CountedQuantity).HasPrecision(18, 3);
        builder.Property(x => x.SystemQuantityAtFinalization).HasPrecision(18, 3);

        // A product may appear at most once per count. Database-enforced because the scan path can
        // fire the same barcode twice in quick succession, and two rows for one product would
        // double-apply its variance at finalization.
        builder.HasIndex(x => new { x.StockCountId, x.ProductId })
            .IsUnique()
            .HasDatabaseName("IX_StockCountItems_StockCountId_ProductId");

        builder.HasOne(x => x.StockCount).WithMany(x => x.Items)
            .HasForeignKey(x => x.StockCountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);

        // VarianceQuantity/IsCounted are computed from the stored columns, never persisted —
        // storing them would let the ledger and the arithmetic drift apart.
        builder.Ignore(x => x.VarianceQuantity);
        builder.Ignore(x => x.IsCounted);
    }
}
