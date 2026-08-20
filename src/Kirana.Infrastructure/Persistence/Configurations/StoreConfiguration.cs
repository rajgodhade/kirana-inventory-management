using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> builder)
    {
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.LegalName).HasMaxLength(200);
        builder.Property(s => s.OwnerName).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Gstin).HasMaxLength(15);
        builder.Property(s => s.StateCode).HasMaxLength(2);
        builder.Property(s => s.GstRegistrationType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.InvoicePrefix).IsRequired().HasMaxLength(20);
        builder.Property(s => s.DefaultInvoiceFormat).IsRequired().HasMaxLength(20);
        builder.Property(s => s.InvoiceFooterText).HasMaxLength(500);
    }
}
