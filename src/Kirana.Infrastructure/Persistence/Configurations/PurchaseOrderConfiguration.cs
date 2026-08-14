using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.Property(p => p.PurchaseOrderNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(p => p.PurchaseOrderNumber).IsUnique();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(p => p.SupplierNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(p => p.SupplierCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(p => p.SupplierContactSnapshot).HasMaxLength(200);
        builder.Property(p => p.CancellationReason).HasMaxLength(500);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.SubTotal).HasPrecision(18, 2);
        builder.Property(p => p.DiscountTotal).HasPrecision(18, 2);
        builder.Property(p => p.TaxableTotal).HasPrecision(18, 2);
        builder.Property(p => p.TaxTotal).HasPrecision(18, 2);
        builder.Property(p => p.RoundOffAmount).HasPrecision(18, 2);
        builder.Property(p => p.GrandTotal).HasPrecision(18, 2);
        builder.HasIndex(p => p.OrderDateUtc);
        builder.HasIndex(p => p.Status);
        builder.HasOne(p => p.Supplier).WithMany(s => s.PurchaseOrders).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.CreatedByUser).WithMany().HasForeignKey(p => p.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.SubmittedByUser).WithMany().HasForeignKey(p => p.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.CancelledByUser).WithMany().HasForeignKey(p => p.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseOrderItemConfiguration : IEntityTypeConfiguration<PurchaseOrderItem>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderItem> builder)
    {
        builder.Property(i => i.ProductNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(i => i.ProductCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(60);
        builder.Property(i => i.HsnCodeSnapshot).HasMaxLength(20);
        builder.Property(i => i.UnitSnapshot).IsRequired().HasMaxLength(20);
        builder.Property(i => i.PricingTypeSnapshot).HasConversion<string>().HasMaxLength(16);
        builder.Property(i => i.OrderedQuantity).HasPrecision(18, 3);
        builder.Property(i => i.UnitCost).HasPrecision(18, 2);
        builder.Property(i => i.DiscountPercent).HasPrecision(5, 2);
        builder.Property(i => i.DiscountAmount).HasPrecision(18, 2);
        builder.Property(i => i.GstRatePercentSnapshot).HasPrecision(5, 2);
        builder.Property(i => i.TaxableAmount).HasPrecision(18, 2);
        builder.Property(i => i.GstAmount).HasPrecision(18, 2);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);
        builder.HasOne(i => i.PurchaseOrder).WithMany(p => p.Items).HasForeignKey(i => i.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.PurchaseOrderId);
        builder.HasIndex(i => i.ProductId);
    }
}
