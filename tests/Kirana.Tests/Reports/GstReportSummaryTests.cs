using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Reports;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Reports;

/// <summary>Phase 18A-6: the centralized GST summary must answer classification, jurisdiction,
/// bill-count, rate-wise and net-of-returns questions purely from stored historical values.</summary>
public sealed class GstReportSummaryTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();

    [Fact]
    public async Task Classification_Jurisdiction_And_BillCounts_SplitStoredTaxableValues()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var b2bCustomer = new Customer
        {
            CustomerCode = "CUST-B2B", Name = "B2B Customer", StateCode = "27",
            Gstin = "27AAPFU0939F1ZV", GstRegistrationType = GstRegistrationType.Regular, IsActive = true,
        };
        var b2cCustomer = new Customer
        {
            CustomerCode = "CUST-B2C", Name = "B2C Customer", StateCode = "29",
            Gstin = null, GstRegistrationType = GstRegistrationType.Unregistered, IsActive = true,
        };
        var product = NewTaxableProduct("PRD-SUMMARY");
        db.AddRange(b2bCustomer, b2cCustomer, product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        await CompleteSaleAsync(saleService, owner.Id, b2bCustomer.Id, product.Id);   // intra / B2B
        await CompleteSaleAsync(saleService, owner.Id, b2cCustomer.Id, product.Id);   // inter / B2C
        // A captured walk-in: B2C by policy, but with NO buyer state the jurisdiction stays
        // explicitly unresolved instead of falling back to the store's state.
        db.Add(new Sale
        {
            InvoiceNumber = "INV-WALKIN",
            SaleDateUtc = DateTime.UtcNow,
            CustomerId = null,
            GstIdentitySnapshotCapturedAtUtc = DateTime.UtcNow,
            StoreStateCodeSnapshot = "27",
            CustomerStateCodeSnapshot = null,
            TaxableTotal = 100m,
            TaxTotal = 18m,
            GrandTotal = 118m,
            Status = SaleStatus.Completed,
            Items =
            [
                new SaleItem
                {
                    ProductId = product.Id,
                    ProductNameSnapshot = "Summary Product",
                    ProductCodeSnapshot = "PRD-SUMMARY",
                    UnitSnapshot = "PCS",
                    GstRatePercentSnapshot = 18m,
                    Quantity = 1m,
                    UnitPriceSnapshot = 100m,
                    TaxableAmount = 100m,
                    GstAmount = 18m,
                    LineTotal = 118m,
                },
            ],
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var report = await new SalesReportService(db, new PermissionEnforcer(db)).GetGstReportAsync(
            ReportDateRange.Resolve(ReportDatePreset.Today), owner.Id);

        Assert.Equal(3, report.SalesBillCount);
        Assert.Equal(1, report.SalesB2bBillCount);
        Assert.Equal(2, report.SalesB2cBillCount);
        Assert.Equal(0, report.SalesUnresolvedBillCount);

        Assert.Equal(100m, report.SalesB2bTaxableValue);
        Assert.Equal(200m, report.SalesB2cTaxableValue);
        Assert.Equal(0m, report.SalesUnresolvedIdentityTaxableValue);
        Assert.Equal(18m, report.SalesB2bGst);
        Assert.Equal(36m, report.SalesB2cGst);
        Assert.Equal(0m, report.SalesUnresolvedIdentityGst);

        Assert.Equal(100m, report.SalesIntraStateTaxableValue);
        Assert.Equal(100m, report.SalesInterStateTaxableValue);
        Assert.Equal(100m, report.SalesUnresolvedJurisdictionTaxableValue);

        var bucket = Assert.Single(report.SalesByRate);
        Assert.Equal(18m, bucket.RatePercent);
        Assert.Equal(300m, bucket.TaxableAmount);
        Assert.Equal(54m, bucket.TaxAmount);
        Assert.Equal(100m, bucket.B2bTaxableAmount);
        Assert.Equal(200m, bucket.B2cTaxableAmount);
        Assert.Equal(0m, bucket.UnresolvedIdentityTaxableAmount);
        Assert.Equal(bucket.TaxAmount, bucket.Cgst + bucket.Sgst + bucket.Igst + bucket.UnresolvedGst);
    }

    [Fact]
    public async Task PartialReturns_ReverseOnlyTheReturnedQuantity()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var permissions = new PermissionEnforcer(db);
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), permissions);
        var returnService = new SalesReturnService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), permissions);

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var product = NewTaxableProduct("PRD-RETURNS", sellingPrice: 10m);
        db.Add(product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        var sale = await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CashierUserId = owner.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 10m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });
        var saleItem = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == sale.Id);

        var reportService = new SalesReportService(db, permissions);
        ReportDateRange Range() => ReportDateRange.Resolve(ReportDatePreset.Today);

        // First partial return: 3 of 10 units.
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = saleItem.Id, Quantity = 3m }],
            ProcessedByUserId = owner.Id,
        });
        var afterFirst = await reportService.GetGstReportAsync(Range(), owner.Id);
        Assert.Equal(100m, afterFirst.SalesTaxableAmount);
        Assert.Equal(18m, afterFirst.SalesGstCollected);
        Assert.Equal(30m, afterFirst.SalesReturnedTaxableValue);
        Assert.Equal(5.4m, afterFirst.SalesReturnedGst);
        Assert.Equal(70m, afterFirst.NetSalesTaxableValue);
        Assert.Equal(12.6m, afterFirst.NetSalesGst);

        // Second partial return: 2 more units — cumulative reversal is 5 units, not 3, not 7,
        // and not the whole sale.
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = saleItem.Id, Quantity = 2m }],
            ProcessedByUserId = owner.Id,
        });
        var afterSecond = await reportService.GetGstReportAsync(Range(), owner.Id);
        Assert.Equal(50m, afterSecond.SalesReturnedTaxableValue);
        Assert.Equal(9m, afterSecond.SalesReturnedGst);
        Assert.Equal(50m, afterSecond.NetSalesTaxableValue);
        Assert.Equal(9m, afterSecond.NetSalesGst);
        Assert.Equal(100m, afterSecond.SalesTaxableAmount);
        Assert.Equal(18m, afterSecond.SalesGstCollected);
    }

    [Fact]
    public async Task Filter_RoundTrip_RestoresOriginalTotalsExactly()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customerA = new Customer
        {
            CustomerCode = "CUST-A", Name = "Customer A", StateCode = "27",
            Gstin = "27AAPFU0939F1ZV", GstRegistrationType = GstRegistrationType.Regular, IsActive = true,
        };
        var customerB = new Customer
        {
            CustomerCode = "CUST-B", Name = "Customer B", StateCode = "27",
            Gstin = "27AAPFU0939F1ZV", GstRegistrationType = GstRegistrationType.Regular, IsActive = true,
        };
        var product = NewTaxableProduct("PRD-FILTER");
        db.AddRange(customerA, customerB, product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        var saleA = await CompleteSaleAsync(saleService, owner.Id, customerA.Id, product.Id);
        await CompleteSaleAsync(saleService, owner.Id, customerB.Id, product.Id);

        var reportService = new SalesReportService(db, new PermissionEnforcer(db));
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        var unfiltered = await reportService.GetGstReportAsync(range, owner.Id);
        var filtered = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { CustomerId = customerA.Id });
        var roundTrip = await reportService.GetGstReportAsync(range, owner.Id);

        Assert.Equal(1, filtered.SalesBillCount);
        Assert.Equal(100m, filtered.SalesTaxableAmount);
        Assert.Equal(2, unfiltered.SalesBillCount);
        Assert.Equal(200m, unfiltered.SalesTaxableAmount);

        // Clearing the filter restores the original totals exactly.
        Assert.Equal(unfiltered.SalesGstCollected, roundTrip.SalesGstCollected);
        Assert.Equal(unfiltered.SalesTaxableAmount, roundTrip.SalesTaxableAmount);
        Assert.Equal(unfiltered.SalesB2bGst, roundTrip.SalesB2bGst);
        Assert.Equal(unfiltered.SalesBillCount, roundTrip.SalesBillCount);
        Assert.Equal(unfiltered.PurchaseGstPaid, roundTrip.PurchaseGstPaid);
        _ = saleA;
    }

    /// <summary>Step 11: a real fingerprint across every table the report could conceivably
    /// touch. The GST report must be strictly read-only.</summary>
    [Fact]
    public async Task GstReport_PerformsNoWrites_AcrossTheFullEntityGraph()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customer = new Customer
        {
            CustomerCode = "CUST-RO", Name = "ReadOnly Customer", StateCode = "27",
            Gstin = "27AAPFU0939F1ZV", GstRegistrationType = GstRegistrationType.Regular, IsActive = true,
        };
        var product = NewTaxableProduct("PRD-READONLY");
        db.AddRange(customer, product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();
        await CompleteSaleAsync(saleService, owner.Id, customer.Id, product.Id);
        db.ChangeTracker.Clear();

        // Row counts alone cannot detect a monetary mutation, so every financial table is also
        // fingerprinted by value sums. Any write performed by the report changes at least one term.
        async Task<(int Sales, int SaleItems, int Purchases, int PurchaseItems, int SalesReturns, int PurchaseReturns,
            int AuditLogs, int Customers, int Suppliers, int Stores, int ProductPrices, int Products,
            decimal SalesTax, decimal SalesGrand, decimal SalesRoundOff, decimal ItemGst, decimal ItemTaxable, decimal PurchaseItemGst)> Fingerprint() =>
            (
                await db.Sales.CountAsync(),
                await db.SaleItems.CountAsync(),
                await db.Purchases.CountAsync(),
                await db.PurchaseItems.CountAsync(),
                await db.SalesReturns.CountAsync(),
                await db.PurchaseReturns.CountAsync(),
                await db.AuditLogs.CountAsync(),
                await db.Customers.CountAsync(),
                await db.Suppliers.CountAsync(),
                await db.Stores.CountAsync(),
                await db.ProductPrices.CountAsync(),
                await db.Products.CountAsync(),
                await db.Sales.SumAsync(s => (decimal?)s.TaxTotal) ?? 0m,
                await db.Sales.SumAsync(s => (decimal?)s.GrandTotal) ?? 0m,
                await db.Sales.SumAsync(s => (decimal?)s.RoundOffAmount) ?? 0m,
                await db.SaleItems.SumAsync(i => (decimal?)i.GstAmount) ?? 0m,
                await db.SaleItems.SumAsync(i => (decimal?)i.TaxableAmount) ?? 0m,
                await db.PurchaseItems.SumAsync(i => (decimal?)i.GstAmount) ?? 0m);

        var before = await Fingerprint();
        await new SalesReportService(db, new PermissionEnforcer(db)).GetGstReportAsync(
            ReportDateRange.Resolve(ReportDatePreset.Today), owner.Id);
        var after = await Fingerprint();

        Assert.Equal(before, after);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static Product NewTaxableProduct(string code, decimal sellingPrice = 100m) => new Product
    {
        ProductCode = code,
        Name = code,
        Unit = UnitOfMeasure.Piece,
        PurchasePrice = sellingPrice,
        Mrp = sellingPrice * 1.3m,
        SellingPrice = sellingPrice,
        GstRatePercent = 18m,
        IsTaxInclusive = false,
        IsActive = true,
    }.WithRetailPrice();

    private static Task<Sale> CompleteSaleAsync(
        SaleService saleService, int ownerId, int customerId, int productId) =>
        saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customerId,
            CashierUserId = ownerId,
            Lines = [new SaleLineInput { ProductId = productId, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });

    public void Dispose() => _fixture.Dispose();
}
