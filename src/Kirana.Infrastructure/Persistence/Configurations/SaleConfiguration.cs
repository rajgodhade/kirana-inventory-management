using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.Property(s => s.InvoiceNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(s => s.InvoiceNumber).IsUnique();
        builder.HasIndex(s => s.SaleDateUtc);

        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(s => s.SubTotal).HasPrecision(18, 2);
        builder.Property(s => s.ItemDiscountTotal).HasPrecision(18, 2);
        builder.Property(s => s.PromotionDiscountTotal).HasPrecision(18, 2);
        builder.Property(s => s.BillDiscountPercent).HasPrecision(5, 2);
        builder.Property(s => s.BillDiscountAmount).HasPrecision(18, 2);
        builder.Property(s => s.TaxableTotal).HasPrecision(18, 2);
        builder.Property(s => s.TaxTotal).HasPrecision(18, 2);
        builder.Property(s => s.RoundOffAmount).HasPrecision(18, 2);
        builder.Property(s => s.GrandTotal).HasPrecision(18, 2);

        builder.HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.CashierUser)
            .WithMany()
            .HasForeignKey(s => s.CashierUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.DiscountAuthorizedByUser)
            .WithMany()
            .HasForeignKey(s => s.DiscountAuthorizedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.PriceOverrideAuthorizedByUser)
            .WithMany()
            .HasForeignKey(s => s.PriceOverrideAuthorizedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.Property(i => i.ProductNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(i => i.ProductCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(i => i.SkuSnapshot).HasMaxLength(60);
        builder.Property(i => i.HsnCodeSnapshot).HasMaxLength(20);
        builder.Property(i => i.UnitSnapshot).IsRequired().HasMaxLength(20);

        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.UnitPriceSnapshot).HasPrecision(18, 2);
        builder.Property(i => i.DiscountPercent).HasPrecision(5, 2);
        builder.Property(i => i.DiscountAmount).HasPrecision(18, 2);
        builder.Property(i => i.PromotionDiscountAmount).HasPrecision(18, 2);
        builder.Property(i => i.GstRatePercentSnapshot).HasPrecision(5, 2);
        builder.Property(i => i.TaxableAmount).HasPrecision(18, 2);
        builder.Property(i => i.GstAmount).HasPrecision(18, 2);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);

        builder.HasOne(i => i.Sale)
            .WithMany(s => s.Items)
            .HasForeignKey(i => i.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(i => i.SaleId);
        builder.HasIndex(i => i.ProductId);
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.AmountTendered).HasPrecision(18, 2);
        builder.Property(p => p.ChangeGiven).HasPrecision(18, 2);
        builder.Property(p => p.ReferenceNumber).HasMaxLength(100);

        builder.HasOne(p => p.Sale)
            .WithMany(s => s.Payments)
            .HasForeignKey(p => p.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.SaleId);
    }
}
