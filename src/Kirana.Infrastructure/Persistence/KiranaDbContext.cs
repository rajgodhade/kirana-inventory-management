using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Infrastructure.Persistence;

public sealed class KiranaDbContext(DbContextOptions<KiranaDbContext> options)
    : DbContext(options), IKiranaDbContext
{
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ProductBatch> ProductBatches => Set<ProductBatch>();
    public DbSet<SequenceCounter> SequenceCounters => Set<SequenceCounter>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CustomerCredit> CustomerCredits => Set<CustomerCredit>();
    public DbSet<HeldBill> HeldBills => Set<HeldBill>();
    public DbSet<HeldBillItem> HeldBillItems => Set<HeldBillItem>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KiranaDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
