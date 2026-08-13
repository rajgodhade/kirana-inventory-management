using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Inventories;

/// <summary>
/// Proves an adjustment is genuinely all-or-nothing. It writes four linked things — the adjustment
/// record, a stock movement, the quantity change, and an audit row — and any subset landing without
/// the others is unrecoverable without manual database surgery: stock moved with no explanation, or
/// an explanation with no stock movement.
///
/// <para>Failures are injected through a decorating <see cref="IKiranaDbContext"/> and a throwing
/// <see cref="IAuditLogger"/>, so the real service and the real SQLite transaction do the work —
/// rather than asserting against a mock that never touched a database.</para>
/// </summary>
public class InventoryAdjustmentAtomicityTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly int _ownerId;
    private int _productId;

    public InventoryAdjustmentAtomicityTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        SeedAsync().GetAwaiter().GetResult();
    }

    private async Task SeedAsync()
    {
        var product = new Product
        {
            ProductCode = "PRD-ATOMADJ", Name = "Atomic Product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10m, Mrp = 15m, SellingPrice = 14m, IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await _fixture.Context.SaveChangesAsync();
        _productId = product.Id;
    }

    private InventoryAdjustmentService CreateService(
        IKiranaDbContext? db = null, IAuditLogger? auditLogger = null) =>
        new(db ?? _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            auditLogger ?? new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context));

    private CreateInventoryAdjustmentRequest Request() => new()
    {
        ProductId = _productId,
        Direction = InventoryAdjustmentDirection.Decrease,
        Quantity = 5m,
        Reason = InventoryAdjustmentReason.Damaged,
        Notes = "Injected failure test",
        PerformedByUserId = _ownerId,
    };

    private async Task AssertNothingAppliedAsync()
    {
        // A fresh context: the failed attempt leaves stale tracked entities in the shared one, and
        // the question is what actually reached the database.
        using var verify = new KiranaDbContext(
            new DbContextOptionsBuilder<KiranaDbContext>()
                .UseSqlite(_fixture.Context.Database.GetDbConnection()).Options);

        Assert.Equal(100m, await verify.Inventories
            .Where(i => i.ProductId == _productId).Select(i => i.QuantityOnHand).FirstAsync());
        Assert.Empty(await verify.StockMovements.ToListAsync());
        Assert.Empty(await verify.InventoryAdjustments.ToListAsync());
        Assert.False(await verify.AuditLogs.AnyAsync(a => a.Action == "InventoryAdjusted"));
    }

    [Fact]
    public async Task RollsBackEverything_WhenTheWriteFails()
    {
        var service = CreateService(db: new FailingSaveDbContext(_fixture.Context));

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(Request()));

        await AssertNothingAppliedAsync();
    }

    /// <summary>The audit write happens LAST, after stock has already been written inside the
    /// transaction. If it throws and the transaction did not roll back, inventory would be
    /// permanently changed with no record of why — the exact scenario the transaction exists for.</summary>
    [Fact]
    public async Task RollsBackTheStockWrite_WhenTheAuditWriteFails()
    {
        var service = CreateService(auditLogger: new ThrowingAuditLogger());

        await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(Request()));

        await AssertNothingAppliedAsync();
    }

    [Fact]
    public async Task CanRetrySuccessfully_AfterAFailedAttempt()
    {
        var failing = CreateService(db: new FailingSaveDbContext(_fixture.Context));
        await Assert.ThrowsAnyAsync<Exception>(() => failing.CreateAsync(Request()));

        // The rollback must leave the row genuinely re-adjustable, not wedged half-done.
        _fixture.Context.ChangeTracker.Clear();
        var healthy = CreateService();
        var adjustment = await healthy.CreateAsync(Request());

        Assert.Equal(95m, adjustment.NewQuantity);
        Assert.Equal(95m, await _fixture.Context.Inventories
            .Where(i => i.ProductId == _productId).Select(i => i.QuantityOnHand).FirstAsync());
        Assert.Single(await _fixture.Context.StockMovements.ToListAsync());
        Assert.Single(await _fixture.Context.InventoryAdjustments.ToListAsync());
    }

    /// <summary>Reconciliation invariant: every adjustment record has exactly one movement, and
    /// every adjustment movement has a matching record. Neither can exist alone.</summary>
    [Fact]
    public async Task EveryAdjustmentHasExactlyOneMatchingMovement()
    {
        var service = CreateService();
        await service.CreateAsync(Request());
        await service.CreateAsync(new CreateInventoryAdjustmentRequest
        {
            ProductId = _productId,
            Direction = InventoryAdjustmentDirection.Increase,
            Quantity = 2m,
            Reason = InventoryAdjustmentReason.Found,
            PerformedByUserId = _ownerId,
        });

        var adjustments = await _fixture.Context.InventoryAdjustments.ToListAsync();
        var movements = await _fixture.Context.StockMovements
            .Where(m => m.ReferenceType == nameof(InventoryAdjustment)).ToListAsync();

        Assert.Equal(2, adjustments.Count);
        Assert.Equal(adjustments.Count, movements.Count);
        foreach (var adjustment in adjustments)
        {
            var movement = Assert.Single(movements, m => m.ReferenceId == adjustment.AdjustmentNumber);
            Assert.Equal(adjustment.SignedQuantity, movement.QuantityChange);
            Assert.Equal(adjustment.PreviousQuantity, movement.PreviousQuantity);
            Assert.Equal(adjustment.NewQuantity, movement.NewQuantity);
        }
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>Passes everything through to the real context but fails the save the adjustment
    /// uses. Everything else — including the transaction — is genuine SQLite.</summary>
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
        public DbSet<InventoryAdjustment> InventoryAdjustments => inner.InventoryAdjustments;

        public Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database => inner.Database;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure: adjustment write refused.");
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
