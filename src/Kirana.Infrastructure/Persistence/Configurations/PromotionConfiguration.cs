using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.Property(x => x.PromotionCode).IsRequired().HasMaxLength(40);
        builder.HasIndex(x => x.PromotionCode).IsUnique();
        builder.Property(x => x.PromotionName).IsRequired().HasMaxLength(160);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.PromotionType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PriorityMode).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.CalculationMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Percentage).HasPrecision(5, 2);
        builder.Property(x => x.FlatAmount).HasPrecision(18, 2);
        builder.Property(x => x.FixedPrice).HasPrecision(18, 2);
        builder.Property(x => x.MaximumDiscount).HasPrecision(18, 2);
        builder.Property(x => x.MinimumBillAmount).HasPrecision(18, 2);
        builder.Property(x => x.MinimumQuantity).HasPrecision(18, 3);
        builder.HasIndex(x => new { x.IsActive, x.Status });
        builder.HasIndex(x => x.Priority);

        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.UpdatedByUser).WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class PromotionScheduleConfiguration : IEntityTypeConfiguration<PromotionSchedule>
{
    public void Configure(EntityTypeBuilder<PromotionSchedule> builder)
    {
        builder.Property(x => x.TimeZoneId).IsRequired().HasMaxLength(100);
        builder.HasOne(x => x.Promotion).WithOne(x => x.Schedule).HasForeignKey<PromotionSchedule>(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.StartAtUtc, x.EndAtUtc });
    }
}

public sealed class PromotionScopeConfiguration : IEntityTypeConfiguration<PromotionScope>
{
    public void Configure(EntityTypeBuilder<PromotionScope> builder)
    {
        builder.Property(x => x.ScopeType).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(x => x.Promotion).WithOne(x => x.Scope).HasForeignKey<PromotionScope>(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PromotionTargetConfiguration : IEntityTypeConfiguration<PromotionTarget>
{
    public void Configure(EntityTypeBuilder<PromotionTarget> builder)
    {
        builder.HasOne(x => x.PromotionScope).WithMany(x => x.Targets).HasForeignKey(x => x.PromotionScopeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Category).WithMany(x => x.PromotionTargets).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Brand).WithMany(x => x.PromotionTargets).HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Product).WithMany(x => x.PromotionTargets).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.PromotionScopeId, x.CategoryId }).IsUnique().HasFilter("\"CategoryId\" IS NOT NULL");
        builder.HasIndex(x => new { x.PromotionScopeId, x.BrandId }).IsUnique().HasFilter("\"BrandId\" IS NOT NULL");
        builder.HasIndex(x => new { x.PromotionScopeId, x.ProductId }).IsUnique().HasFilter("\"ProductId\" IS NOT NULL");
    }
}

public sealed class PromotionRuleConfiguration : IEntityTypeConfiguration<PromotionRule>
{
    public void Configure(EntityTypeBuilder<PromotionRule> builder)
    {
        builder.Property(x => x.RuleType).IsRequired().HasMaxLength(80);
        builder.Property(x => x.RuleValue).HasMaxLength(1000);
        builder.HasOne(x => x.Promotion).WithMany(x => x.Rules).HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SaleItemPromotionConfiguration : IEntityTypeConfiguration<SaleItemPromotion>
{
    public void Configure(EntityTypeBuilder<SaleItemPromotion> builder)
    {
        builder.Property(x => x.PromotionCodeSnapshot).IsRequired().HasMaxLength(40);
        builder.Property(x => x.PromotionNameSnapshot).IsRequired().HasMaxLength(160);
        builder.Property(x => x.PromotionTypeSnapshot).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.CalculationModeSnapshot).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.HasOne(x => x.SaleItem).WithMany(x => x.Promotions).HasForeignKey(x => x.SaleItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Promotion).WithMany(x => x.SaleApplications).HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.PromotionId);
    }
}
