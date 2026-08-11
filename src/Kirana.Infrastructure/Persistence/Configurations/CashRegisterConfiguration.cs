using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class CashRegisterSessionConfiguration : IEntityTypeConfiguration<CashRegisterSession>
{
    public void Configure(EntityTypeBuilder<CashRegisterSession> builder)
    {
        builder.Property(x => x.RegisterName).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(x => x.OpenedAtUtc);
        builder.HasIndex(x => x.BusinessDate);
        builder.HasIndex(x => new { x.StoreId, x.Status });
        builder.HasIndex(x => x.StoreId).IsUnique().HasFilter("Status = 'Open'")
            .HasDatabaseName("IX_CashRegisterSessions_StoreId_Open");

        foreach (var property in new[]
        {
            nameof(CashRegisterSession.OpeningCash), nameof(CashRegisterSession.TotalSales),
            nameof(CashRegisterSession.CashSales), nameof(CashRegisterSession.UpiSales),
            nameof(CashRegisterSession.CardSales), nameof(CashRegisterSession.CustomerCreditSales),
            nameof(CashRegisterSession.TotalReturns), nameof(CashRegisterSession.CashRefunds),
            nameof(CashRegisterSession.CashCreditRepayments), nameof(CashRegisterSession.CashIn),
            nameof(CashRegisterSession.CashOut), nameof(CashRegisterSession.SupplierCashPayments),
            nameof(CashRegisterSession.ExpectedCash),
            nameof(CashRegisterSession.ActualCash), nameof(CashRegisterSession.Variance),
        }) builder.Property(property).HasPrecision(18, 2);

        builder.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OpenedByUser).WithMany().HasForeignKey(x => x.OpenedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ClosedByUser).WithMany().HasForeignKey(x => x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.Property(x => x.OperationId).IsRequired();
        builder.HasIndex(x => x.OperationId).IsUnique();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(500);
        builder.HasIndex(x => new { x.RegisterSessionId, x.OccurredAtUtc });
        builder.HasOne(x => x.RegisterSession).WithMany(x => x.Movements)
            .HasForeignKey(x => x.RegisterSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PerformedByUser).WithMany()
            .HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
