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
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<SequenceCounter> SequenceCounters => Set<SequenceCounter>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CustomerCredit> CustomerCredits => Set<CustomerCredit>();
    public DbSet<HeldBill> HeldBills => Set<HeldBill>();
    public DbSet<HeldBillItem> HeldBillItems => Set<HeldBillItem>();

    public DbSet<CreditPayment> CreditPayments => Set<CreditPayment>();
    public DbSet<CreditPaymentAllocation> CreditPaymentAllocations => Set<CreditPaymentAllocation>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();

    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnItem> SalesReturnItems => Set<SalesReturnItem>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionSchedule> PromotionSchedules => Set<PromotionSchedule>();
    public DbSet<PromotionScope> PromotionScopes => Set<PromotionScope>();
    public DbSet<PromotionTarget> PromotionTargets => Set<PromotionTarget>();
    public DbSet<PromotionRule> PromotionRules => Set<PromotionRule>();
    public DbSet<SaleItemPromotion> SaleItemPromotions => Set<SaleItemPromotion>();
    public DbSet<CashRegisterSession> CashRegisterSessions => Set<CashRegisterSession>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KiranaDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
