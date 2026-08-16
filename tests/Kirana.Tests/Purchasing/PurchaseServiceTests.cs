using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Purchasing;

public class PurchaseServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly PurchaseService _sut;
    private readonly SupplierService _supplierService;
    private readonly int _ownerId;

    public PurchaseServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        // Phase 16A-2: cash transactions require an open register. These tests use cash as
        // fixture setup for something else, so the shop is simply open for business.
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var sequenceGenerator = new EfSequenceGenerator(_fixture.Context);
        var auditLogger = new EfAuditLogger(_fixture.Context);
        var permissionEnforcer = new PermissionEnforcer(_fixture.Context);
        _sut = new PurchaseService(_fixture.Context, sequenceGenerator, auditLogger, permissionEnforcer);
        _supplierService = new SupplierService(_fixture.Context, sequenceGenerator, auditLogger, permissionEnforcer);
    }

    private async Task<Supplier> SeedSupplierAsync(string name = "Sharma Distributors") =>
        await _supplierService.CreateAsync(new CreateSupplierRequest { Name = name, PerformedByUserId = _ownerId });

    private async Task<Product> SeedProductAsync(
        string name = "Tata Salt 1kg", decimal price = 25, decimal stock = 0,
        UnitOfMeasure unit = UnitOfMeasure.Piece, decimal? gstRate = null, bool isTaxInclusive = false, bool tracksBatches = false,
        UnitOfMeasure? purchasePackUnit = null, decimal? purchasePackSize = null)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = name,
            Unit = unit,
            PurchasePackUnit = purchasePackUnit,
            PurchasePackSize = purchasePackSize,
            PurchasePrice = price,
            Mrp = price + 5,
            SellingPrice = price + 3,
            GstRatePercent = gstRate,
            IsTaxInclusive = isTaxInclusive,
            TracksBatches = tracksBatches,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private static CreatePurchaseRequest BasicRequest(int supplierId, int productId, decimal quantity, decimal unitPrice, int? userId) => new()
    {
        SupplierId = supplierId,
        Lines = [new PurchaseLineInput { ProductId = productId, Quantity = quantity, UnitPrice = unitPrice }],
        CreatedByUserId = userId,
    };

    [Fact]
    public async Task FinalizePurchaseAsync_CreatesPurchaseWithCorrectTotals()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 50);

        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 10, 50, _ownerId));

        Assert.Equal(500m, purchase.SubTotal);
        Assert.Equal(500m, purchase.GrandTotal);
        Assert.Single(purchase.Items);
        Assert.Equal(PurchaseStatus.Completed, purchase.Status);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_GeneratesPurchaseNumberInExpectedFormat()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();

        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 10, _ownerId));

        Assert.Matches(@"^PUR-\d{4}-\d{6}$", purchase.PurchaseNumber);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_GeneratesSequentialPurchaseNumbers()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();

        var first = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 10, _ownerId));
        var second = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 10, _ownerId));

        var year = DateTime.UtcNow.Year;
        Assert.Equal($"PUR-{year}-000001", first.PurchaseNumber);
        Assert.Equal($"PUR-{year}-000002", second.PurchaseNumber);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_IncreasesStockAndWritesStockMovement()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(stock: 20);

        await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 15, 10, _ownerId));

        var inventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id);
        Assert.Equal(35m, inventory.QuantityOnHand);

        var movement = await _fixture.Context.StockMovements.SingleAsync(m => m.ProductId == product.Id);
        Assert.Equal(StockMovementType.Purchase, movement.MovementType);
        Assert.Equal(15m, movement.QuantityChange);
        Assert.Equal(20m, movement.PreviousQuantity);
        Assert.Equal(35m, movement.NewQuantity);
        Assert.Equal("Purchase", movement.ReferenceType);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_StoresHistoricalSnapshot_ThatSurvivesLaterProductEdits()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(name: "Original Name", price: 30);

        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 30, _ownerId));
        var purchaseId = purchase.Id;

        product.Name = "Renamed Product";
        product.PurchasePrice = 999;
        await _fixture.Context.SaveChangesAsync();

        var reloaded = await _sut.GetByIdAsync(purchaseId, _ownerId);
        var item = reloaded!.Items.Single();

        Assert.Equal("Original Name", item.ProductNameSnapshot);
        Assert.Equal(30m, item.PurchasePriceSnapshot);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_AppliesGst_WhenProductHasGstRate()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100, gstRate: 12, isTaxInclusive: false);

        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 100, _ownerId));

        Assert.Equal(12m, purchase.TaxTotal);
        Assert.Equal(112m, purchase.GrandTotal);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_AppliesItemDiscount()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1, UnitPrice = 100, DiscountPercent = 10 }],
            CreatedByUserId = _ownerId,
        };

        var purchase = await _sut.FinalizePurchaseAsync(request);

        Assert.Equal(10m, purchase.DiscountTotal);
        Assert.Equal(90m, purchase.GrandTotal);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_CreatesNewBatch_WhenProductTracksBatches()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(tracksBatches: true);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput
            {
                ProductId = product.Id, Quantity = 20, UnitPrice = 15,
                BatchNumber = "BATCH-001", ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddDays(90)),
            }],
            CreatedByUserId = _ownerId,
        };

        await _sut.FinalizePurchaseAsync(request);

        var batch = await _fixture.Context.ProductBatches.SingleAsync(b => b.ProductId == product.Id);
        Assert.Equal("BATCH-001", batch.BatchNumber);
        Assert.Equal(20m, batch.Quantity);
        Assert.Equal(15m, batch.PurchasePrice);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_IncreasesExistingBatch_WhenBatchNumberMatches()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(tracksBatches: true);

        var firstRequest = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 10, UnitPrice = 15, BatchNumber = "BATCH-001" }],
            CreatedByUserId = _ownerId,
        };
        await _sut.FinalizePurchaseAsync(firstRequest);

        // Each purchase runs in its own scoped DbContext in the real app, so the second one must
        // re-read the batch from the database rather than relying on it still being change-tracked
        // from the first. Clearing the tracker reproduces that, and catches a missing Include.
        _fixture.Context.ChangeTracker.Clear();

        var secondRequest = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 5, UnitPrice = 16, BatchNumber = "BATCH-001" }],
            CreatedByUserId = _ownerId,
        };
        await _sut.FinalizePurchaseAsync(secondRequest);

        var batches = await _fixture.Context.ProductBatches.Where(b => b.ProductId == product.Id).ToListAsync();
        var batch = Assert.Single(batches);
        Assert.Equal(15m, batch.Quantity);
        Assert.Equal(16m, batch.PurchasePrice);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_DoesNotCreateBatch_WhenProductDoesNotTrackBatches()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(tracksBatches: false);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 10, UnitPrice = 15, BatchNumber = "BATCH-001" }],
            CreatedByUserId = _ownerId,
        };
        await _sut.FinalizePurchaseAsync(request);

        Assert.False(await _fixture.Context.ProductBatches.AnyAsync(b => b.ProductId == product.Id));
    }

    [Fact]
    public async Task FinalizePurchaseAsync_RecordsInitialPayment_AndUpdatesOutstandingBalances()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1, UnitPrice = 100 }],
            AmountPaid = 40,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = _ownerId,
        };

        var purchase = await _sut.FinalizePurchaseAsync(request);

        Assert.Equal(40m, purchase.AmountPaid);
        Assert.Equal(60m, purchase.OutstandingAmount);

        var updatedSupplier = await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id);
        Assert.Equal(60m, updatedSupplier.OutstandingBalance);

        var payment = await _fixture.Context.SupplierPayments.SingleAsync();
        Assert.Equal(40m, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(purchase.Id, payment.PurchaseId);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_FullyPaid_LeavesZeroOutstanding()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1, UnitPrice = 100 }],
            AmountPaid = 100,
            PaymentMethod = PaymentMethod.Upi,
            CreatedByUserId = _ownerId,
        };

        var purchase = await _sut.FinalizePurchaseAsync(request);

        Assert.Equal(0m, purchase.OutstandingAmount);
        var updatedSupplier = await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id);
        Assert.Equal(0m, updatedSupplier.OutstandingBalance);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_NoPayment_LeavesFullOutstanding()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100);

        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 100, _ownerId));

        Assert.Equal(100m, purchase.OutstandingAmount);
        Assert.Equal(0m, purchase.AmountPaid);
        Assert.False(await _fixture.Context.SupplierPayments.AnyAsync());
    }

    [Fact]
    public async Task FinalizePurchaseAsync_Throws_WhenAmountPaidExceedsGrandTotal()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1, UnitPrice = 100 }],
            AmountPaid = 150,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.FinalizePurchaseAsync(request));
    }

    [Fact]
    public async Task FinalizePurchaseAsync_Throws_WhenAmountPaidWithoutPaymentMethod()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1, UnitPrice = 100 }],
            AmountPaid = 50,
            CreatedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.FinalizePurchaseAsync(request));
    }

    [Fact]
    public async Task FinalizePurchaseAsync_Throws_WhenCartEmpty()
    {
        var supplier = await SeedSupplierAsync();

        var request = new CreatePurchaseRequest { SupplierId = supplier.Id, Lines = [], CreatedByUserId = _ownerId };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.FinalizePurchaseAsync(request));
    }

    [Fact]
    public async Task FinalizePurchaseAsync_Throws_WhenSupplierInactive()
    {
        var supplier = await SeedSupplierAsync();
        await _supplierService.SetActiveAsync(supplier.Id, isActive: false, _ownerId);
        var product = await SeedProductAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 10, _ownerId)));
    }

    [Fact]
    public async Task FinalizePurchaseAsync_RollsBackEverything_WhenOneLineIsInvalid()
    {
        var supplier = await SeedSupplierAsync();
        var goodProduct = await SeedProductAsync(name: "Good", price: 10);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines =
            [
                new PurchaseLineInput { ProductId = goodProduct.Id, Quantity = 2, UnitPrice = 10 },
                new PurchaseLineInput { ProductId = 999, Quantity = 1, UnitPrice = 10 },
            ],
            CreatedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.FinalizePurchaseAsync(request));

        Assert.Equal(0, await _fixture.Context.Purchases.CountAsync());
        Assert.Equal(0, await _fixture.Context.StockMovements.CountAsync());
        var goodInventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == goodProduct.Id);
        Assert.Equal(0m, goodInventory.QuantityOnHand);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_Throws_WhenPerformerLacksPermission()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 10, cashier.Id)));
    }

    [Fact]
    public async Task FinalizePurchaseAsync_LogsAuditEntry()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 20);

        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 20, _ownerId));

        var entry = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "PurchaseCreated");
        Assert.Equal(purchase.Id.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_AuditsTheUpFrontPayment_AsAPaymentInItsOwnRight()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1, UnitPrice = 100 }],
            AmountPaid = 40,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = _ownerId,
        };

        await _sut.FinalizePurchaseAsync(request);

        // Both the purchase and the money that changed hands must be independently auditable.
        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "PurchaseCreated"));
        var paymentEntry = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "SupplierPaymentRecorded");
        var payment = await _fixture.Context.SupplierPayments.SingleAsync();
        Assert.Equal(payment.Id.ToString(), paymentEntry.EntityId);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_DoesNotAuditAPayment_WhenNothingWasPaidUpFront()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100);

        await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 100, _ownerId));

        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "SupplierPaymentRecorded"));
    }

    [Fact]
    public async Task RecordPaymentAsync_PartialPayment_ReducesOutstandingBalance()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 200);
        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 200, _ownerId));

        await _sut.RecordPaymentAsync(new RecordSupplierPaymentRequest
        {
            SupplierId = supplier.Id,
            PurchaseId = purchase.Id,
            Amount = 80,
            Method = PaymentMethod.Cash,
            RecordedByUserId = _ownerId,
        });

        var updatedPurchase = await _sut.GetByIdAsync(purchase.Id, _ownerId);
        Assert.Equal(80m, updatedPurchase!.AmountPaid);
        Assert.Equal(120m, updatedPurchase.OutstandingAmount);

        var updatedSupplier = await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id);
        Assert.Equal(120m, updatedSupplier.OutstandingBalance);
    }

    [Fact]
    public async Task RecordPaymentAsync_FullPayment_ZeroesOutstandingBalance()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 200);
        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 200, _ownerId));

        await _sut.RecordPaymentAsync(new RecordSupplierPaymentRequest
        {
            SupplierId = supplier.Id,
            PurchaseId = purchase.Id,
            Amount = 200,
            Method = PaymentMethod.Upi,
            RecordedByUserId = _ownerId,
        });

        var updatedPurchase = await _sut.GetByIdAsync(purchase.Id, _ownerId);
        Assert.Equal(0m, updatedPurchase!.OutstandingAmount);
    }

    [Fact]
    public async Task RecordPaymentAsync_WithoutPurchaseId_StillReducesSupplierBalance()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 200);
        await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 200, _ownerId));

        await _sut.RecordPaymentAsync(new RecordSupplierPaymentRequest
        {
            SupplierId = supplier.Id,
            Amount = 50,
            Method = PaymentMethod.Cash,
            RecordedByUserId = _ownerId,
        });

        var updatedSupplier = await _fixture.Context.Suppliers.SingleAsync(s => s.Id == supplier.Id);
        Assert.Equal(150m, updatedSupplier.OutstandingBalance);
    }

    [Fact]
    public async Task RecordPaymentAsync_Throws_WhenAmountNotPositive()
    {
        var supplier = await SeedSupplierAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.RecordPaymentAsync(new RecordSupplierPaymentRequest
        {
            SupplierId = supplier.Id,
            Amount = 0,
            Method = PaymentMethod.Cash,
            RecordedByUserId = _ownerId,
        }));
    }

    [Fact]
    public async Task RecordPaymentAsync_Throws_WhenPerformerLacksPermission()
    {
        var supplier = await SeedSupplierAsync();
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.RecordPaymentAsync(new RecordSupplierPaymentRequest
        {
            SupplierId = supplier.Id,
            Amount = 10,
            Method = PaymentMethod.Cash,
            RecordedByUserId = cashier.Id,
        }));
    }

    [Fact]
    public async Task RecordPaymentAsync_LogsAuditEntry()
    {
        var supplier = await SeedSupplierAsync();

        var payment = await _sut.RecordPaymentAsync(new RecordSupplierPaymentRequest
        {
            SupplierId = supplier.Id,
            Amount = 25,
            Method = PaymentMethod.Cash,
            RecordedByUserId = _ownerId,
        });

        var entry = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "SupplierPaymentRecorded");
        Assert.Equal(payment.Id.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task SupplierLedger_ShowsPurchaseAndPaymentWithRunningBalance()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 300);

        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 300, _ownerId));
        await _sut.RecordPaymentAsync(new RecordSupplierPaymentRequest
        {
            SupplierId = supplier.Id,
            PurchaseId = purchase.Id,
            Amount = 100,
            Method = PaymentMethod.Cash,
            RecordedByUserId = _ownerId,
        });

        var ledger = await _supplierService.GetLedgerAsync(supplier.Id, _ownerId);

        Assert.Equal(2, ledger.Count);
        Assert.Equal("Purchase", ledger[0].EntryType);
        Assert.Equal(300m, ledger[0].DebitAmount);
        Assert.Equal(300m, ledger[0].RunningBalance);
        Assert.Equal("Payment", ledger[1].EntryType);
        Assert.Equal(100m, ledger[1].CreditAmount);
        Assert.Equal(200m, ledger[1].RunningBalance);
    }

    [Fact]
    public async Task SearchAsync_FiltersBySupplier()
    {
        var supplierA = await SeedSupplierAsync("Supplier A");
        var supplierB = await SeedSupplierAsync("Supplier B");
        var product = await SeedProductAsync();

        await _sut.FinalizePurchaseAsync(BasicRequest(supplierA.Id, product.Id, 1, 10, _ownerId));
        await _sut.FinalizePurchaseAsync(BasicRequest(supplierB.Id, product.Id, 1, 10, _ownerId));

        var results = await _sut.SearchAsync(new PurchaseSearchQuery { SupplierId = supplierA.Id }, _ownerId);

        Assert.Single(results);
        Assert.Equal(supplierA.Id, results[0].SupplierId);
    }

    [Fact]
    public async Task SearchAsync_OutstandingOnly_ExcludesFullyPaidPurchases()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(price: 100);

        var paidRequest = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1, UnitPrice = 100 }],
            AmountPaid = 100,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = _ownerId,
        };
        await _sut.FinalizePurchaseAsync(paidRequest);

        var unpaid = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 100, _ownerId));

        var results = await _sut.SearchAsync(new PurchaseSearchQuery { OutstandingOnly = true }, _ownerId);

        Assert.Single(results);
        Assert.Equal(unpaid.Id, results[0].Id);
    }

    [Fact]
    public async Task SearchAsync_Throws_WhenReaderLacksPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();

        // Purchases expose negotiated purchase prices and outstanding amounts.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.SearchAsync(new PurchaseSearchQuery(), cashier.Id));
    }

    [Fact]
    public async Task GetByIdAsync_Throws_WhenReaderLacksPermission()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();
        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 1, 10, _ownerId));
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetByIdAsync(purchase.Id, cashier.Id));
    }

    // ===================== Phase 13A: units, pack sizes & unit conversion =====================

    [Fact]
    public async Task FinalizePurchaseAsync_AcceptsPlainQuantity_WhenNoPackFieldsSent()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(stock: 10, purchasePackUnit: UnitOfMeasure.Box, purchasePackSize: 12);

        var purchase = await _sut.FinalizePurchaseAsync(BasicRequest(supplier.Id, product.Id, 5, 10, _ownerId));

        Assert.Equal(5m, purchase.Items.Single().Quantity);
        var inventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id);
        Assert.Equal(15m, inventory.QuantityOnHand);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_ConvertsPackQuantity_ToBaseUnitInventoryAndStockMovement()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(stock: 0, purchasePackUnit: UnitOfMeasure.Box, purchasePackSize: 12);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput
            {
                ProductId = product.Id, Quantity = 120, UnitPrice = 10,
                PurchasedPackUnit = UnitOfMeasure.Box, PurchasedPackQuantity = 10,
            }],
            CreatedByUserId = _ownerId,
        };

        var purchase = await _sut.FinalizePurchaseAsync(request);

        Assert.Equal(120m, purchase.Items.Single().Quantity);

        var inventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id);
        Assert.Equal(120m, inventory.QuantityOnHand);

        var movement = await _fixture.Context.StockMovements.SingleAsync(m => m.ProductId == product.Id);
        Assert.Equal(StockMovementType.Purchase, movement.MovementType);
        Assert.Equal(120m, movement.QuantityChange);
        Assert.Equal(0m, movement.PreviousQuantity);
        Assert.Equal(120m, movement.NewQuantity);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_PopulatesPackSnapshotColumns_OnPurchaseItem()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(purchasePackUnit: UnitOfMeasure.Box, purchasePackSize: 12);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput
            {
                ProductId = product.Id, Quantity = 120, UnitPrice = 10,
                PurchasedPackUnit = UnitOfMeasure.Box, PurchasedPackQuantity = 10,
            }],
            CreatedByUserId = _ownerId,
        };

        var purchase = await _sut.FinalizePurchaseAsync(request);

        var item = await _fixture.Context.PurchaseItems.SingleAsync(i => i.PurchaseId == purchase.Id);
        Assert.Equal("Box", item.PurchasedPackUnitSnapshot);
        Assert.Equal(10m, item.PurchasedPackQuantitySnapshot);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_Throws_WhenPackUnitDoesNotMatchProductConfiguration()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(purchasePackUnit: UnitOfMeasure.Box, purchasePackSize: 12);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput
            {
                ProductId = product.Id, Quantity = 240, UnitPrice = 10,
                PurchasedPackUnit = UnitOfMeasure.Carton, PurchasedPackQuantity = 10,
            }],
            CreatedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.FinalizePurchaseAsync(request));
    }

    [Fact]
    public async Task FinalizePurchaseAsync_Throws_WhenProductHasNoPackConfigured_ButPackFieldsSent()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync();

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput
            {
                ProductId = product.Id, Quantity = 120, UnitPrice = 10,
                PurchasedPackUnit = UnitOfMeasure.Box, PurchasedPackQuantity = 10,
            }],
            CreatedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.FinalizePurchaseAsync(request));
    }

    [Fact]
    public async Task FinalizePurchaseAsync_Throws_WhenSubmittedQuantityDisagreesWithPackMath()
    {
        // Fault injection: a tampered/buggy payload claims 10 Box but only sends a base-unit
        // Quantity of 100 (should be 120) — the server must catch this, not trust the client.
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(purchasePackUnit: UnitOfMeasure.Box, purchasePackSize: 12);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput
            {
                ProductId = product.Id, Quantity = 100, UnitPrice = 10,
                PurchasedPackUnit = UnitOfMeasure.Box, PurchasedPackQuantity = 10,
            }],
            CreatedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.FinalizePurchaseAsync(request));

        // Prove the rejection has teeth: nothing was written for the mismatched line.
        Assert.False(await _fixture.Context.Purchases.AnyAsync(p => p.SupplierId == supplier.Id));
        var inventory = await _fixture.Context.Inventories.SingleOrDefaultAsync(i => i.ProductId == product.Id);
        Assert.True(inventory is null || inventory.QuantityOnHand == 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task FinalizePurchaseAsync_Throws_WhenPackQuantityIsZeroOrNegative(decimal packQuantity)
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(purchasePackUnit: UnitOfMeasure.Box, purchasePackSize: 12);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput
            {
                ProductId = product.Id, Quantity = 12, UnitPrice = 10,
                PurchasedPackUnit = UnitOfMeasure.Box, PurchasedPackQuantity = packQuantity,
            }],
            CreatedByUserId = _ownerId,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.FinalizePurchaseAsync(request));
    }

    [Fact]
    public async Task FinalizePurchaseAsync_TopsUpBatchQuantity_UsingConvertedBaseQuantity_WhenTracksBatches()
    {
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(tracksBatches: true, purchasePackUnit: UnitOfMeasure.Box, purchasePackSize: 12);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput
            {
                ProductId = product.Id, Quantity = 120, UnitPrice = 10, BatchNumber = "BATCH-PACK-1",
                PurchasedPackUnit = UnitOfMeasure.Box, PurchasedPackQuantity = 10,
            }],
            CreatedByUserId = _ownerId,
        };

        await _sut.FinalizePurchaseAsync(request);

        var batch = await _fixture.Context.ProductBatches.SingleAsync(b => b.ProductId == product.Id);
        Assert.Equal(120m, batch.Quantity);
    }

    [Fact]
    public async Task FinalizePurchaseAsync_RemainingStock_AfterPackPurchaseAndSale()
    {
        // Purchase: 10 Box x 12 Piece = 120 Piece. Sale: 5 Piece. Remaining: 115 Piece.
        var supplier = await SeedSupplierAsync();
        var product = await SeedProductAsync(stock: 0, purchasePackUnit: UnitOfMeasure.Box, purchasePackSize: 12);

        var request = new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            Lines = [new PurchaseLineInput
            {
                ProductId = product.Id, Quantity = 120, UnitPrice = 10,
                PurchasedPackUnit = UnitOfMeasure.Box, PurchasedPackQuantity = 10,
            }],
            CreatedByUserId = _ownerId,
        };
        await _sut.FinalizePurchaseAsync(request);

        var inventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id);
        inventory.QuantityOnHand -= 5; // simulate a 5-Piece sale without pulling in SaleService here
        await _fixture.Context.SaveChangesAsync();

        Assert.Equal(115m, inventory.QuantityOnHand);
    }

    public void Dispose() => _fixture.Dispose();
}
