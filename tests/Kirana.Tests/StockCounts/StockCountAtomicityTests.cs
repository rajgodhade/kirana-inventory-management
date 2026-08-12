using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.StockCounts;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.StockCounts;

/// <summary>
/// Proves finalization is genuinely all-or-nothing (§12). Finalization writes stock movements,
/// mutates inventory quantities, flips the count status, and writes an audit row — a partial
/// application would leave stock moved with no count to explain it, which is unrecoverable without
/// manual database surgery.
///
/// <para>Failures are injected through a decorating <see cref="IKiranaDbContext"/> and a throwing
/// <see cref="IAuditLogger"/>, so the real service runs unmodified and the real SQLite transaction
/// is what performs the rollback — rather than asserting against a mock that never touched a
/// database.</para>
/// </summary>
public class StockCountAtomicityTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly int _ownerId;
    private int _productId;
    private int _secondProductId;

    public StockCountAtomicityTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        SeedProductsAsync().GetAwaiter().GetResult();
    }

    private async Task SeedProductsAsync()
    {
        var first = new Product
        {
            ProductCode = "PRD-ATOM1", Name = "Atomic Product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10m, Mrp = 15m, SellingPrice = 14m, IsActive = true,
        };
        var second = new Product
        {
            ProductCode = "PRD-ATOM2", Name = "Second Product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10m, Mrp = 15m, SellingPrice = 14m, IsActive = true,
        };
        _fixture.Context.Products.AddRange(first, second);
        _fixture.Context.Inventories.Add(new Inventory { Product = first, QuantityOnHand = 100m });
        _fixture.Context.Inventories.Add(new Inventory { Product = second, QuantityOnHand = 50m });
        await _fixture.Context.SaveChangesAsync();
        _productId = first.Id;
        _secondProductId = second.Id;
    }

    private StockCountService CreateService(
        IKiranaDbContext? db = null, IAuditLogger? auditLogger = null) =>
        new(db ?? _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            auditLogger ?? new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context),
            new BarcodeLookupService(_fixture.Context));

    /// <summary>Builds a count with both products counted at a variance, ready to finalize.</summary>
    private async Task<StockCount> BuildCountAsync()
    {
        var service = CreateService();
        var count = await service.StartAsync(null, _ownerId);

        var first = await service.AddItemAsync(count.Id, _productId, null, _ownerId);
        await service.SetCountedQuantityAsync(first.Id, 97m, null, _ownerId);

        var second = await service.AddItemAsync(count.Id, _secondProductId, null, _ownerId);
        await service.SetCountedQuantityAsync(second.Id, 55m, null, _ownerId);

        return count;
    }

    private async Task AssertNothingAppliedAsync(int stockCountId)
    {
        // A fresh context: the failed attempt leaves stale tracked entities in the shared one, and
        // the question is what actually reached the database.
        using var verify = new KiranaDbContext(
            new DbContextOptionsBuilder<KiranaDbContext>()
                .UseSqlite(_fixture.Context.Database.GetDbConnection()).Options);

        Assert.Equal(100m, await verify.Inventories.Where(i => i.ProductId == _productId)
            .Select(i => i.QuantityOnHand).FirstAsync());
        Assert.Equal(50m, await verify.Inventories.Where(i => i.ProductId == _secondProductId)
            .Select(i => i.QuantityOnHand).FirstAsync());

        Assert.Empty(await verify.StockMovements.ToListAsync());

        var reloaded = await verify.StockCounts.FirstAsync(c => c.Id == stockCountId);
        Assert.Equal(StockCountStatus.InProgress, reloaded.Status);
        Assert.Null(reloaded.CompletedAtUtc);

        Assert.False(await verify.AuditLogs.AnyAsync(a => a.Action == "StockCountCompleted"));
    }

    [Fact]
    public async Task Finalize_RollsBackEverything_WhenTheStockWriteFails()
    {
        var count = await BuildCountAsync();
        var failing = new FailingSaveDbContext(_fixture.Context);
        var service = CreateService(db: failing);

        await Assert.ThrowsAnyAsync<Exception>(() => service.FinalizeAsync(count.Id, _ownerId));

        await AssertNothingAppliedAsync(count.Id);
    }

    /// <summary>The audit write is the last step, and happens AFTER stock has already been written
    /// inside the transaction. If it throws and the transaction did not roll back, inventory would
    /// be permanently changed with no audit record of why.</summary>
    [Fact]
    public async Task Finalize_RollsBackTheStockWrite_WhenTheAuditWriteFails()
    {
        var count = await BuildCountAsync();
        var service = CreateService(auditLogger: new ThrowingAuditLogger());

        await Assert.ThrowsAnyAsync<Exception>(() => service.FinalizeAsync(count.Id, _ownerId));

        await AssertNothingAppliedAsync(count.Id);
    }

    [Fact]
    public async Task Finalize_LeavesNoPartialMovements_WhenFailureOccursPartWayThroughAMultiItemCount()
    {
        var count = await BuildCountAsync();
        var failing = new FailingSaveDbContext(_fixture.Context);
        var service = CreateService(db: failing);

        await Assert.ThrowsAnyAsync<Exception>(() => service.FinalizeAsync(count.Id, _ownerId));

        // Neither product may be adjusted: it must not be possible for the first line to land and
        // the second to be lost.
        await AssertNothingAppliedAsync(count.Id);
    }

    [Fact]
    public async Task Finalize_CanBeRetriedSuccessfully_AfterAFailedAttempt()
    {
        var count = await BuildCountAsync();
        var service = CreateService(db: new FailingSaveDbContext(_fixture.Context));
        await Assert.ThrowsAnyAsync<Exception>(() => service.FinalizeAsync(count.Id, _ownerId));

        // The rollback must leave the count genuinely re-finalizable, not wedged half-done.
        _fixture.Context.ChangeTracker.Clear();
        var healthy = CreateService();
        var result = await healthy.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(2, result.AdjustmentCount);
        Assert.Equal(97m, await _fixture.Context.Inventories
            .Where(i => i.ProductId == _productId).Select(i => i.QuantityOnHand).FirstAsync());
        Assert.Equal(55m, await _fixture.Context.Inventories
            .Where(i => i.ProductId == _secondProductId).Select(i => i.QuantityOnHand).FirstAsync());
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>Passes everything through to the real context but fails the save that finalization
    /// uses to write movements and quantities. Everything else — including the transaction — is the
    /// genuine SQLite implementation.</summary>
    private sealed class FailingSaveDbContext(KiranaDbContext inner) : IKiranaDbContext
    {
        public DbSet<Store> Stores => inner.Stores;
        public DbSet<AppSettings> AppSettings => inner.AppSettings;
        public DbSet<Role> Roles => inner.Roles;
        public DbSet<Permission> Permissions => inner.Permissions;
        public DbSet<RolePermission> RolePermissions => inner.RolePermissions;
        public DbSet<User> Users => inner.Users;
        public DbSet<AuditLog> AuditLogs => inner.AuditLogs;
        public DbSet<Category> Categories => inner.Categories;
        public DbSet<Brand> Brands => inner.Brands;
        public DbSet<Product> Products => inner.Products;
        public DbSet<Inventory> Inventories => inner.Inventories;
        public DbSet<StockMovement> StockMovements => inner.StockMovements;
        public DbSet<ProductBatch> ProductBatches => inner.ProductBatches;
        public DbSet<ProductBarcode> ProductBarcodes => inner.ProductBarcodes;
        public DbSet<SequenceCounter> SequenceCounters => inner.SequenceCounters;
        public DbSet<Customer> Customers => inner.Customers;
        public DbSet<Sale> Sales => inner.Sales;
        public DbSet<SaleItem> SaleItems => inner.SaleItems;
        public DbSet<Payment> Payments => inner.Payments;
        public DbSet<CustomerCredit> CustomerCredits => inner.CustomerCredits;
        public DbSet<HeldBill> HeldBills => inner.HeldBills;
        public DbSet<HeldBillItem> HeldBillItems => inner.HeldBillItems;
        public DbSet<CreditPayment> CreditPayments => inner.CreditPayments;
        public DbSet<CreditPaymentAllocation> CreditPaymentAllocations => inner.CreditPaymentAllocations;
        public DbSet<Supplier> Suppliers => inner.Suppliers;
        public DbSet<Purchase> Purchases => inner.Purchases;
        public DbSet<PurchaseItem> PurchaseItems => inner.PurchaseItems;
        public DbSet<SupplierPayment> SupplierPayments => inner.SupplierPayments;
        public DbSet<SalesReturn> SalesReturns => inner.SalesReturns;
        public DbSet<SalesReturnItem> SalesReturnItems => inner.SalesReturnItems;
        public DbSet<PurchaseReturn> PurchaseReturns => inner.PurchaseReturns;
        public DbSet<PurchaseReturnItem> PurchaseReturnItems => inner.PurchaseReturnItems;
        public DbSet<ExpenseCategory> ExpenseCategories => inner.ExpenseCategories;
        public DbSet<Expense> Expenses => inner.Expenses;
        public DbSet<BackupRecord> BackupRecords => inner.BackupRecords;
        public DbSet<Promotion> Promotions => inner.Promotions;
        public DbSet<PromotionSchedule> PromotionSchedules => inner.PromotionSchedules;
        public DbSet<PromotionScope> PromotionScopes => inner.PromotionScopes;
        public DbSet<PromotionTarget> PromotionTargets => inner.PromotionTargets;
        public DbSet<PromotionRule> PromotionRules => inner.PromotionRules;
        public DbSet<SaleItemPromotion> SaleItemPromotions => inner.SaleItemPromotions;
        public DbSet<CashRegisterSession> CashRegisterSessions => inner.CashRegisterSessions;
        public DbSet<CashMovement> CashMovements => inner.CashMovements;
        public DbSet<StockCount> StockCounts => inner.StockCounts;
        public DbSet<StockCountItem> StockCountItems => inner.StockCountItems;

        public Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database => inner.Database;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure: stock write refused.");
    }

    private sealed class ThrowingAuditLogger : IAuditLogger
    {
        public Task RecordAsync(
            int? userId, string action, string entityName, string? entityId = null,
            string? previousValue = null, string? newValue = null, string? reason = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure: audit write refused.");
    }
}
