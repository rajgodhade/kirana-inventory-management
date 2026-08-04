using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.Property(r => r.ReturnNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(r => r.ReturnNumber).IsUnique();

        builder.Property(r => r.InvoiceNumberSnapshot).IsRequired().HasMaxLength(30);
        builder.HasIndex(r => r.InvoiceNumberSnapshot);
        builder.HasIndex(r => r.ReturnDateUtc);
        builder.HasIndex(r => r.SaleId);
        builder.HasIndex(r => r.CustomerId);

        builder.Property(r => r.RefundMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ReferenceNumber).HasMaxLength(100);
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.Notes).HasMaxLength(1000);

        builder.Property(r => r.TotalReturnAmount).HasPrecision(18, 2);
        builder.Property(r => r.RefundAmount).HasPrecision(18, 2);

        // Restrict everywhere: a return is a financial record and must never be cascade-deleted
        // along with the sale, customer or user it references.
        builder.HasOne(r => r.Sale).WithMany().HasForeignKey(r => r.SaleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Customer).WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.ProcessedByUser).WithMany().HasForeignKey(r => r.ProcessedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.AuthorizedByUser).WithMany().HasForeignKey(r => r.AuthorizedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SalesReturnItemConfiguration : IEntityTypeConfiguration<SalesReturnItem>
{
    public void Configure(EntityTypeBuilder<SalesReturnItem> builder)
    {
        builder.Property(i => i.ProductNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(i => i.ProductCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(60);
        builder.Property(i => i.UnitSnapshot).IsRequired().HasMaxLength(20);
        builder.Property(i => i.BatchNumber).HasMaxLength(60);
        builder.Property(i => i.Reason).HasMaxLength(500);

        builder.Property(i => i.Disposition).HasConversion<string>().HasMaxLength(20);

        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.UnitPriceSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.GstRatePercentSnapshot).HasPrecision(5, 2);
        builder.Property(i => i.LineRefundAmount).HasPrecision(18, 2);

        builder.HasOne(i => i.SalesReturn)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.SalesReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexed because the per-line returned-quantity cap is computed by summing over SaleItemId.
        builder.HasIndex(i => i.SaleItemId);
        builder.HasOne(i => i.SaleItem).WithMany().HasForeignKey(i => i.SaleItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseReturnConfiguration : IEntityTypeConfiguration<PurchaseReturn>
{
    public void Configure(EntityTypeBuilder<PurchaseReturn> builder)
    {
        builder.Property(r => r.ReturnNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(r => r.ReturnNumber).IsUnique();

        builder.Property(r => r.PurchaseNumberSnapshot).IsRequired().HasMaxLength(30);
        builder.HasIndex(r => r.PurchaseNumberSnapshot);
        builder.HasIndex(r => r.ReturnDateUtc);
        builder.HasIndex(r => r.PurchaseId);
        builder.HasIndex(r => r.SupplierId);

        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.Notes).HasMaxLength(1000);
        builder.Property(r => r.TotalReturnAmount).HasPrecision(18, 2);

        builder.HasOne(r => r.Purchase).WithMany().HasForeignKey(r => r.PurchaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.ProcessedByUser).WithMany().HasForeignKey(r => r.ProcessedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseReturnItemConfiguration : IEntityTypeConfiguration<PurchaseReturnItem>
{
    public void Configure(EntityTypeBuilder<PurchaseReturnItem> builder)
    {
        builder.Property(i => i.ProductNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(i => i.ProductCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(60);
        builder.Property(i => i.UnitSnapshot).IsRequired().HasMaxLength(20);
        builder.Property(i => i.BatchNumber).HasMaxLength(60);
        builder.Property(i => i.Reason).HasMaxLength(500);

        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.PurchasePriceSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.GstRatePercentSnapshot).HasPrecision(5, 2);
        builder.Property(i => i.LineReturnAmount).HasPrecision(18, 2);

        builder.HasOne(i => i.PurchaseReturn)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.PurchaseReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.PurchaseItemId);
        builder.HasOne(i => i.PurchaseItem).WithMany().HasForeignKey(i => i.PurchaseItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
