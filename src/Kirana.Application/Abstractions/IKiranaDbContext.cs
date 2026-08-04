using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Abstractions;

/// <summary>
/// Application-facing view of the EF Core context. Application services depend on this
/// interface, not the concrete Infrastructure DbContext, so business logic stays testable
/// without a real SQLite file.
/// </summary>
public interface IKiranaDbContext
{
    DbSet<Store> Stores { get; }
    DbSet<AppSettings> AppSettings { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<User> Users { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DbSet<Category> Categories { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Product> Products { get; }
    DbSet<Inventory> Inventories { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<ProductBatch> ProductBatches { get; }
    DbSet<SequenceCounter> SequenceCounters { get; }

    DbSet<Customer> Customers { get; }
    DbSet<Sale> Sales { get; }
    DbSet<SaleItem> SaleItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<CustomerCredit> CustomerCredits { get; }
    DbSet<HeldBill> HeldBills { get; }
    DbSet<HeldBillItem> HeldBillItems { get; }

    DbSet<Supplier> Suppliers { get; }
    DbSet<Purchase> Purchases { get; }
    DbSet<PurchaseItem> PurchaseItems { get; }
    DbSet<SupplierPayment> SupplierPayments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
