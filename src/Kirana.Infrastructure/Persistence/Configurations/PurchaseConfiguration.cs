using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.Property(p => p.PurchaseNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(p => p.PurchaseNumber).IsUnique();
        builder.Property(p => p.SupplierInvoiceNumber).HasMaxLength(60);
        builder.HasIndex(p => p.SupplierInvoiceNumber);
        builder.HasIndex(p => p.PurchaseDateUtc);

        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Notes).HasMaxLength(1000);

        builder.Property(p => p.SubTotal).HasPrecision(18, 2);
        builder.Property(p => p.DiscountTotal).HasPrecision(18, 2);
        builder.Property(p => p.TaxableTotal).HasPrecision(18, 2);
        builder.Property(p => p.TaxTotal).HasPrecision(18, 2);
        builder.Property(p => p.RoundOffAmount).HasPrecision(18, 2);
        builder.Property(p => p.GrandTotal).HasPrecision(18, 2);
        builder.Property(p => p.AmountPaid).HasPrecision(18, 2);
        builder.Property(p => p.OutstandingAmount).HasPrecision(18, 2);

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.Purchases)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.CreatedByUser)
            .WithMany()
            .HasForeignKey(p => p.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.SupplierId);
    }
}

public sealed class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> builder)
    {
        builder.Property(i => i.ProductNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(i => i.ProductCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(60);
        builder.Property(i => i.HsnCodeSnapshot).HasMaxLength(20);
        builder.Property(i => i.UnitSnapshot).IsRequired().HasMaxLength(20);
        builder.Property(i => i.BatchNumber).HasMaxLength(60);

        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.PurchasePriceSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.DiscountPercent).HasPrecision(5, 2);
        builder.Property(i => i.DiscountAmount).HasPrecision(18, 2);
        builder.Property(i => i.GstRatePercentSnapshot).HasPrecision(5, 2);
        builder.Property(i => i.TaxableAmount).HasPrecision(18, 2);
        builder.Property(i => i.GstAmount).HasPrecision(18, 2);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);

        builder.HasOne(i => i.Purchase)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PurchaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.PurchaseId);
        builder.HasIndex(i => i.ProductId);
    }
}

public sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.ReferenceNumber).HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.HasIndex(p => p.PaymentDateUtc);

        builder.HasOne(p => p.Supplier)
            .WithMany(s => s.Payments)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Purchase)
            .WithMany(pu => pu.Payments)
            .HasForeignKey(p => p.PurchaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.RecordedByUser)
            .WithMany()
            .HasForeignKey(p => p.RecordedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.SupplierId);
        builder.HasIndex(p => p.PurchaseId);
    }
}
