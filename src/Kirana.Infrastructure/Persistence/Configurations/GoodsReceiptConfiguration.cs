using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.Property(g => g.GoodsReceiptNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(g => g.GoodsReceiptNumber).IsUnique();
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(g => g.SupplierNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(g => g.SupplierCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(g => g.Notes).HasMaxLength(1000);
        builder.Property(g => g.CancellationReason).HasMaxLength(500);
        builder.HasIndex(g => g.ReceivedAtUtc);
        builder.HasIndex(g => g.Status);
        builder.HasIndex(g => g.PurchaseOrderId);
        builder.HasOne(g => g.PurchaseOrder).WithMany(p => p.GoodsReceipts).HasForeignKey(g => g.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.Supplier).WithMany(s => s.GoodsReceipts).HasForeignKey(g => g.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.CreatedByUser).WithMany().HasForeignKey(g => g.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.CompletedByUser).WithMany().HasForeignKey(g => g.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.CancelledByUser).WithMany().HasForeignKey(g => g.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(g => g.Purchase).WithOne(p => p.GoodsReceipt).HasForeignKey<Purchase>(p => p.GoodsReceiptId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GoodsReceiptItemConfiguration : IEntityTypeConfiguration<GoodsReceiptItem>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptItem> builder)
    {
        builder.Property(i => i.ProductNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(i => i.ProductCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(60);
        builder.Property(i => i.UnitSnapshot).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.OrderedQuantitySnapshot).HasPrecision(18, 3);
        builder.Property(i => i.ReceivedQuantity).HasPrecision(18, 3);
        builder.Property(i => i.BarcodeSnapshot).HasMaxLength(48);
        builder.Property(i => i.Notes).HasMaxLength(500);
        builder.HasOne(i => i.GoodsReceipt).WithMany(g => g.Items).HasForeignKey(i => i.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.PurchaseOrderItem).WithMany(p => p.GoodsReceiptItems).HasForeignKey(i => i.PurchaseOrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.GoodsReceiptId);
        builder.HasIndex(i => i.PurchaseOrderItemId);
        builder.HasIndex(i => i.ProductId);
    }
}
