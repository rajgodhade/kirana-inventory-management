using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Returns;

/// <summary>
/// Sales returns (PRD §33). Sales are made through the real <see cref="SaleService"/> rather than
/// hand-built rows, so these tests also prove the Phase 4 sale path and the Phase 9 return path
/// agree about quantities, snapshots and stock.
/// </summary>
public class SalesReturnServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly SalesReturnService _sut;
    private readonly SaleService _saleService;
    private readonly int _ownerId;

    public SalesReturnServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var seq = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var enforcer = new PermissionEnforcer(_fixture.Context);

        _saleService = new SaleService(_fixture.Context, seq, audit, enforcer);
        _sut = new SalesReturnService(_fixture.Context, seq, audit, enforcer);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Product> SeedProductAsync(
        decimal price = 100, decimal stock = 100, bool tracksBatches = false, UnitOfMeasure unit = UnitOfMeasure.Piece)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Return Test Product",
            Unit = unit,
            PurchasePrice = price * 0.7m,
            Mrp = price + 10,
            SellingPrice = price,
            TracksBatches = tracksBatches,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private async Task<Customer> SeedCustomerAsync()
    {
        var customer = new Customer { CustomerCode = "CUST-000001", Name = "Return Customer", IsActive = true };
        _fixture.Context.Customers.Add(customer);
        await _fixture.Context.SaveChangesAsync();
        return customer;
    }

    private Task<Sale> SellAsync(Product product, decimal quantity, int? customerId = null) =>
        _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customerId,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = quantity }],
            Payments =
            [
                new SalePaymentInput
                {
                    Method = PaymentMethod.Cash,
                    Amount = product.SellingPrice * quantity,
                    AmountTendered = product.SellingPrice * quantity,
                },
            ],
            CashierUserId = _ownerId,
        });

    private async Task<decimal> StockAsync(int productId) =>
        (await _fixture.Context.Inventories.AsNoTracking().FirstAsync(i => i.ProductId == productId)).QuantityOnHand;

    private static SalesReturnLineInput Line(
        int saleItemId, decimal quantity,
        ReturnDisposition disposition = ReturnDisposition.ReturnToStock, string? batch = null) => new()
        {
            SaleItemId = saleItemId,
            Quantity = quantity,
            Disposition = disposition,
            BatchNumber = batch,
        };

    private Task<SalesReturn> ReturnAsync(
        Sale sale, IEnumerable<SalesReturnLineInput> lines,
        RefundMethod method = RefundMethod.Cash, decimal? refundAmount = null, int? authorizedBy = null) =>
        _sut.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id,
            Lines = lines.ToList(),
            RefundMethod = method,
            RefundAmount = refundAmount,
            ProcessedByUserId = _ownerId,
            AuthorizedByUserId = authorizedBy,
        });

    // ---------------------------------------------------------------- full & partial

    [Fact]
    public async Task FullReturn_RestoresAllStockAndRefundsFullValue()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 5);
        Assert.Equal(45m, await StockAsync(product.Id));

        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync(i => i.SaleId == sale.Id);
        var salesReturn = await ReturnAsync(sale, [Line(saleItem.Id, 5)]);

        Assert.Equal(50m, await StockAsync(product.Id));
        Assert.Equal(500m, salesReturn.TotalReturnAmount);
        Assert.Equal(500m, salesReturn.RefundAmount);
        Assert.StartsWith("SRN-", salesReturn.ReturnNumber);
    }

    [Fact]
    public async Task PartialReturn_RestoresOnlyReturnedQuantity()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 5);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var salesReturn = await ReturnAsync(sale, [Line(saleItem.Id, 2)]);

        Assert.Equal(47m, await StockAsync(product.Id));
        Assert.Equal(200m, salesReturn.TotalReturnAmount);
    }

    [Fact]
    public async Task MultiplePartialReturns_AccumulateUpToTheSoldQuantity()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 5);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await ReturnAsync(sale, [Line(saleItem.Id, 2)]);
        await ReturnAsync(sale, [Line(saleItem.Id, 3)]);

        Assert.Equal(50m, await StockAsync(product.Id));

        var returnable = await _sut.GetReturnableSaleAsync(sale.Id, _ownerId);
        Assert.Equal(0m, returnable!.Lines.Single().ReturnableQuantity);
        Assert.True(returnable.Lines.Single().IsFullyReturned);
    }

    // ---------------------------------------------------------------- quantity validation

    [Fact]
    public async Task Return_Throws_WhenExceedingSoldQuantity()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 3);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ReturnAsync(sale, [Line(saleItem.Id, 4)]));
        Assert.Contains("returnable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Return_Throws_WhenCumulativeReturnsExceedSoldQuantity()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 5);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await ReturnAsync(sale, [Line(saleItem.Id, 4)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ReturnAsync(sale, [Line(saleItem.Id, 2)]));
    }

    [Fact]
    public async Task Return_Throws_WhenOneRequestSplitsAnOverReturnAcrossTwoLines()
    {
        // Guards the cap being applied per-line-input rather than per-sale-line.
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 3);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ReturnAsync(sale, [Line(saleItem.Id, 2), Line(saleItem.Id, 2)]));
    }

    [Fact]
    public async Task Return_Throws_OnNonPositiveQuantity()
    {
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, 2);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => ReturnAsync(sale, [Line(saleItem.Id, 0)]));
        await Assert.ThrowsAsync<ArgumentException>(() => ReturnAsync(sale, [Line(saleItem.Id, -1)]));
    }

    [Fact]
    public async Task Return_Throws_WhenLineBelongsToADifferentSale()
    {
        var product = await SeedProductAsync();
        var first = await SellAsync(product, 2);
        var second = await SellAsync(product, 2);

        var otherLine = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync(i => i.SaleId == second.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ReturnAsync(first, [Line(otherLine.Id, 1)]));
    }

    [Fact]
    public async Task Return_Throws_OnFractionalQuantityForWholeUnitProduct()
    {
        var product = await SeedProductAsync(unit: UnitOfMeasure.Piece);
        var sale = await SellAsync(product, 3);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => ReturnAsync(sale, [Line(saleItem.Id, 1.5m)]));
    }

    // ---------------------------------------------------------------- disposition

    [Fact]
    public async Task DamagedReturn_DoesNotIncreaseSellableStock()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 5);
        Assert.Equal(45m, await StockAsync(product.Id));

        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();
        await ReturnAsync(sale, [Line(saleItem.Id, 2, ReturnDisposition.Damaged)]);

        // Sellable stock is unchanged: the goods came back but cannot be sold again.
        Assert.Equal(45m, await StockAsync(product.Id));
    }

    [Fact]
    public async Task DamagedReturn_RecordsTheWriteOffInTheStockLedger()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 5);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await ReturnAsync(sale, [Line(saleItem.Id, 2, ReturnDisposition.Damaged)]);

        var movements = await _fixture.Context.StockMovements.AsNoTracking()
            .Where(m => m.ReferenceType == nameof(SalesReturn))
            .ToListAsync();

        // Return in, write-off out — net zero on hand, but the damage is visible and reportable.
        Assert.Equal(2m, movements.Single(m => m.MovementType == StockMovementType.SalesReturn).QuantityChange);
        Assert.Equal(-2m, movements.Single(m => m.MovementType == StockMovementType.Damaged).QuantityChange);
    }

    [Fact]
    public async Task MixedDisposition_OnlyResellableQuantityReturnsToStock()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 6);
        Assert.Equal(44m, await StockAsync(product.Id));

        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();
        await ReturnAsync(sale,
        [
            Line(saleItem.Id, 4),
            Line(saleItem.Id, 2, ReturnDisposition.Damaged),
        ]);

        Assert.Equal(48m, await StockAsync(product.Id));
    }

    [Fact]
    public async Task ReturnToStock_TopsUpAnExistingBatch()
    {
        var product = await SeedProductAsync(price: 100, stock: 50, tracksBatches: true);
        _fixture.Context.ProductBatches.Add(new ProductBatch { Product = product, BatchNumber = "B1", Quantity = 50 });
        await _fixture.Context.SaveChangesAsync();

        var sale = await SellAsync(product, 5);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await ReturnAsync(sale, [Line(saleItem.Id, 3, batch: "B1")]);

        var batches = await _fixture.Context.ProductBatches.AsNoTracking().Where(b => b.ProductId == product.Id).ToListAsync();
        Assert.Equal(53m, Assert.Single(batches).Quantity);
    }

    [Fact]
    public async Task DamagedReturn_DoesNotTouchBatchQuantities()
    {
        var product = await SeedProductAsync(price: 100, stock: 50, tracksBatches: true);
        _fixture.Context.ProductBatches.Add(new ProductBatch { Product = product, BatchNumber = "B1", Quantity = 50 });
        await _fixture.Context.SaveChangesAsync();

        var sale = await SellAsync(product, 5);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await ReturnAsync(sale, [Line(saleItem.Id, 3, ReturnDisposition.Damaged, batch: "B1")]);

        var batch = await _fixture.Context.ProductBatches.AsNoTracking().FirstAsync(b => b.ProductId == product.Id);
        Assert.Equal(50m, batch.Quantity);
    }

    // ---------------------------------------------------------------- refunds

    [Theory]
    [InlineData(RefundMethod.Cash)]
    [InlineData(RefundMethod.Upi)]
    [InlineData(RefundMethod.Card)]
    public async Task Refund_RecordsTheChosenMethodAndFullValue(RefundMethod method)
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 4);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var salesReturn = await ReturnAsync(sale, [Line(saleItem.Id, 2)], method);

        Assert.Equal(method, salesReturn.RefundMethod);
        Assert.Equal(200m, salesReturn.RefundAmount);
    }

    [Fact]
    public async Task NoRefund_ReturnsStockButRefundsNothing()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 4);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var salesReturn = await ReturnAsync(sale, [Line(saleItem.Id, 2)], RefundMethod.None);

        Assert.Equal(0m, salesReturn.RefundAmount);
        Assert.Equal(200m, salesReturn.TotalReturnAmount);
        Assert.Equal(48m, await StockAsync(product.Id));
    }

    [Fact]
    public async Task PartialRefund_BelowGoodsValueIsAllowed()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 4);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var salesReturn = await ReturnAsync(sale, [Line(saleItem.Id, 2)], RefundMethod.Cash, refundAmount: 150m);

        Assert.Equal(200m, salesReturn.TotalReturnAmount);
        Assert.Equal(150m, salesReturn.RefundAmount);
    }

    [Fact]
    public async Task Refund_Throws_WhenExceedingTheValueOfReturnedGoods()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 4);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ReturnAsync(sale, [Line(saleItem.Id, 2)], RefundMethod.Cash, refundAmount: 500m));
        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StoreCredit_ReducesWhatTheCustomerOwes()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync(price: 100, stock: 50);

        // Put the customer in debt first, so the store credit has something to settle against.
        customer.CreditBalance = 400m;
        await _fixture.Context.SaveChangesAsync();

        var sale = await SellAsync(product, 3, customer.Id);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync(i => i.SaleId == sale.Id);

        await ReturnAsync(sale, [Line(saleItem.Id, 2)], RefundMethod.StoreCredit);

        var updated = await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id);
        Assert.Equal(200m, updated.CreditBalance);
    }

    [Fact]
    public async Task StoreCredit_Throws_ForAWalkInSale()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 3);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ReturnAsync(sale, [Line(saleItem.Id, 1)], RefundMethod.StoreCredit));
        Assert.Contains("customer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- integrity

    [Fact]
    public async Task Return_NeverAltersTheOriginalSale()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 5);

        var before = await _fixture.Context.Sales.AsNoTracking()
            .Include(s => s.Items).Include(s => s.Payments).FirstAsync(s => s.Id == sale.Id);

        var saleItem = before.Items.First();
        await ReturnAsync(sale, [Line(saleItem.Id, 3)]);

        var after = await _fixture.Context.Sales.AsNoTracking()
            .Include(s => s.Items).Include(s => s.Payments).FirstAsync(s => s.Id == sale.Id);

        Assert.Equal(before.InvoiceNumber, after.InvoiceNumber);
        Assert.Equal(before.GrandTotal, after.GrandTotal);
        Assert.Equal(before.Items.Single().Quantity, after.Items.Single().Quantity);
        Assert.Equal(before.Payments.Sum(p => p.Amount), after.Payments.Sum(p => p.Amount));
    }

    [Fact]
    public async Task ReturnLines_SnapshotTheOriginalSaleNotTheLiveProduct()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 4);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        // Rename and reprice the product after the sale — the return must not follow.
        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        tracked.Name = "Renamed Product";
        tracked.SellingPrice = 999m;
        await _fixture.Context.SaveChangesAsync();

        var salesReturn = await ReturnAsync(sale, [Line(saleItem.Id, 2)]);

        var line = salesReturn.Items.Single();
        Assert.Equal("Return Test Product", line.ProductNameSnapshot);
        Assert.Equal(100m, line.UnitPriceSnapshot);
        Assert.Equal(200m, line.LineRefundAmount);
    }

    [Fact]
    public async Task FailedReturn_LeavesNoStockOrRecordBehind()
    {
        var product = await SeedProductAsync(price: 100, stock: 50);
        var sale = await SellAsync(product, 3);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var stockBefore = await StockAsync(product.Id);
        var movementsBefore = await _fixture.Context.StockMovements.CountAsync();

        // Second line pushes the total over the sold quantity — the whole request must be rejected,
        // including the first line that on its own would have been fine.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ReturnAsync(sale, [Line(saleItem.Id, 1), Line(saleItem.Id, 5)]));

        Assert.Equal(stockBefore, await StockAsync(product.Id));
        Assert.Equal(movementsBefore, await _fixture.Context.StockMovements.CountAsync());
        Assert.Empty(await _fixture.Context.SalesReturns.ToListAsync());
        Assert.Empty(await _fixture.Context.SalesReturnItems.ToListAsync());
    }

    // ---------------------------------------------------------------- lookup

    [Fact]
    public async Task FindReturnableSales_ByInvoiceNumber()
    {
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, 2);

        var found = await _sut.FindReturnableSalesAsync(new SaleLookupQuery { SearchText = sale.InvoiceNumber }, _ownerId);

        Assert.Equal(sale.Id, Assert.Single(found).SaleId);
    }

    [Fact]
    public async Task FindReturnableSales_ByProductName()
    {
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, 2);

        var found = await _sut.FindReturnableSalesAsync(new SaleLookupQuery { SearchText = "Return Test" }, _ownerId);

        Assert.Contains(found, s => s.SaleId == sale.Id);
    }

    [Fact]
    public async Task FindReturnableSales_ByBarcode()
    {
        var product = await SeedProductAsync();
        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == product.Id);
        tracked.Barcode = "8901234567890";
        await _fixture.Context.SaveChangesAsync();

        var sale = await SellAsync(product, 1);

        var found = await _sut.FindReturnableSalesAsync(new SaleLookupQuery { SearchText = "8901234567890" }, _ownerId);

        Assert.Contains(found, s => s.SaleId == sale.Id);
    }

    [Fact]
    public async Task FindReturnableSales_ByCustomer()
    {
        var customer = await SeedCustomerAsync();
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, 2, customer.Id);
        await SellAsync(product, 1);

        var byId = await _sut.FindReturnableSalesAsync(new SaleLookupQuery { CustomerId = customer.Id }, _ownerId);
        var byName = await _sut.FindReturnableSalesAsync(new SaleLookupQuery { SearchText = "Return Customer" }, _ownerId);

        Assert.Equal(sale.Id, Assert.Single(byId).SaleId);
        Assert.Equal(sale.Id, Assert.Single(byName).SaleId);
    }

    [Fact]
    public async Task ReturnableSale_ReportsRemainingQuantityPerLine()
    {
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, 5);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();
        await ReturnAsync(sale, [Line(saleItem.Id, 2)]);

        var line = (await _sut.GetReturnableSaleAsync(sale.Id, _ownerId))!.Lines.Single();

        Assert.Equal(5m, line.SoldQuantity);
        Assert.Equal(2m, line.AlreadyReturnedQuantity);
        Assert.Equal(3m, line.ReturnableQuantity);
    }

    // ---------------------------------------------------------------- audit

    [Fact]
    public async Task Return_WritesAuditEntry()
    {
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, 3);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var salesReturn = await ReturnAsync(sale, [Line(saleItem.Id, 1)]);

        var audit = await _fixture.Context.AuditLogs.SingleOrDefaultAsync(
            a => a.Action == "SalesReturnProcessed" && a.EntityId == salesReturn.Id.ToString());
        Assert.NotNull(audit);
        Assert.Contains(salesReturn.ReturnNumber, audit!.NewValue);
    }

    [Fact]
    public async Task DamagedReturn_WritesASeparateDamagedStockAudit()
    {
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, 3);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await ReturnAsync(sale, [Line(saleItem.Id, 1, ReturnDisposition.Damaged)]);

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "DamagedStockRecorded"));
    }

    [Fact]
    public async Task ResellableOnlyReturn_WritesNoDamagedStockAudit()
    {
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, 3);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await ReturnAsync(sale, [Line(saleItem.Id, 1)]);

        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "DamagedStockRecorded"));
    }

    [Fact]
    public async Task AuthorizedRefund_IsAuditedAgainstTheAuthorizer()
    {
        var product = await SeedProductAsync();
        var sale = await SellAsync(product, 3);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        await ReturnAsync(sale, [Line(saleItem.Id, 1)], authorizedBy: _ownerId);

        var audit = await _fixture.Context.AuditLogs.SingleOrDefaultAsync(a => a.Action == "RefundAuthorized");
        Assert.NotNull(audit);
        Assert.Equal(_ownerId, audit!.UserId);
    }

    [Fact]
    public async Task ReturnNumbers_AreSequentialAndUnique()
    {
        var product = await SeedProductAsync(stock: 200);
        var sale = await SellAsync(product, 6);
        var saleItem = await _fixture.Context.SaleItems.AsNoTracking().FirstAsync();

        var first = await ReturnAsync(sale, [Line(saleItem.Id, 1)]);
        var second = await ReturnAsync(sale, [Line(saleItem.Id, 1)]);

        Assert.Equal("SRN-000001", first.ReturnNumber);
        Assert.Equal("SRN-000002", second.ReturnNumber);
    }

    public void Dispose() => _fixture.Dispose();
}
