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
        // A partial return against customer A's bill — reversal must follow the filters too.
        var saleAItem = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == saleA.Id);
        var returnService = new SalesReturnService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = saleA.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = saleAItem.Id, Quantity = 1m }],
            ProcessedByUserId = owner.Id,
        });

        var reportService = new SalesReportService(db, new PermissionEnforcer(db));
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        var unfiltered = await reportService.GetGstReportAsync(range, owner.Id);
        var filtered = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { CustomerId = customerA.Id });
        var roundTrip = await reportService.GetGstReportAsync(range, owner.Id);

        Assert.Equal(1, filtered.SalesBillCount);
        Assert.Equal(100m, filtered.SalesTaxableAmount);
        // Customer A's bill carries the partial return — filtering to it includes its reversal.
        Assert.Equal(100m, filtered.SalesReturnedTaxableValue);
        Assert.Equal(18m, filtered.SalesReturnedGst);
        Assert.Equal(2, unfiltered.SalesBillCount);
        Assert.Equal(200m, unfiltered.SalesTaxableAmount);
        Assert.Equal(100m, unfiltered.SalesReturnedTaxableValue);
        Assert.Equal(18m, unfiltered.SalesReturnedGst);

        // Clearing the filter restores the original totals exactly.
        Assert.Equal(unfiltered.SalesGstCollected, roundTrip.SalesGstCollected);
        Assert.Equal(unfiltered.SalesTaxableAmount, roundTrip.SalesTaxableAmount);
        Assert.Equal(unfiltered.SalesB2bGst, roundTrip.SalesB2bGst);
        Assert.Equal(unfiltered.SalesBillCount, roundTrip.SalesBillCount);
        Assert.Equal(unfiltered.PurchaseGstPaid, roundTrip.PurchaseGstPaid);
        Assert.Equal(unfiltered.SalesReturnedTaxableValue, roundTrip.SalesReturnedTaxableValue);
        Assert.Equal(unfiltered.SalesReturnedGst, roundTrip.SalesReturnedGst);
        Assert.Equal(unfiltered.NetSalesTaxableValue, roundTrip.NetSalesTaxableValue);
        Assert.Equal(unfiltered.NetSalesGst, roundTrip.NetSalesGst);
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

    [Fact]
    public async Task CustomerFilter_ReturnReversalFollowsOriginatingSaleEligibility()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));
        var returnService = new SalesReturnService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customerA = NewCustomer("CUST-FA", "Filter Customer A");
        var customerB = NewCustomer("CUST-FB", "Filter Customer B");
        var product = NewTaxableProduct("PRD-CUSTFILTER");
        db.AddRange(customerA, customerB, product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        var saleA = await CompleteSaleAsync(saleService, owner.Id, customerA.Id, product.Id);
        await CompleteSaleAsync(saleService, owner.Id, customerB.Id, product.Id);
        var saleAItem = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == saleA.Id);

        // Return one unit of Sale A (full line: taxable 100, GST 18).
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = saleA.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = saleAItem.Id, Quantity = 1m }],
            ProcessedByUserId = owner.Id,
        });

        var reportService = new SalesReportService(db, new PermissionEnforcer(db));
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        // Filter to Customer B: Sale A is excluded, so Sale A's return must NOT subtract anything.
        var filteredToB = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { CustomerId = customerB.Id });
        Assert.Equal(100m, filteredToB.SalesTaxableAmount);
        Assert.Equal(0m, filteredToB.SalesReturnedTaxableValue);
        Assert.Equal(0m, filteredToB.SalesReturnedGst);
        Assert.Equal(18m, filteredToB.NetSalesGst);

        // Filter to Customer A: Sale A is included, so its return reverses correctly.
        var filteredToA = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { CustomerId = customerA.Id });
        Assert.Equal(100m, filteredToA.SalesTaxableAmount);
        Assert.Equal(100m, filteredToA.SalesReturnedTaxableValue);
        Assert.Equal(18m, filteredToA.SalesReturnedGst);
        Assert.Equal(0m, filteredToA.NetSalesGst);
    }

    [Fact]
    public async Task PriceLevelFilter_NarrowsGstSales_AndTheirReturns()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));
        var returnService = new SalesReturnService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customer = NewCustomer("CUST-PL", "Price Level Customer");
        var product = NewTaxableProduct("PRD-PLFILTER");
        product.WithWholesalePrice(100m);
        db.AddRange(customer, product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        // Sale A: Wholesale. Sale B: Retail.
        var saleA = await CompleteSaleAsync(saleService, owner.Id, customer.Id, product.Id, PriceLevel.Wholesale);
        await CompleteSaleAsync(saleService, owner.Id, customer.Id, product.Id, PriceLevel.Retail);
        var saleAItem = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == saleA.Id);

        // Return the full wholesale line (taxable 100, GST 18).
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = saleA.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = saleAItem.Id, Quantity = 1m }],
            ProcessedByUserId = owner.Id,
        });

        var reportService = new SalesReportService(db, new PermissionEnforcer(db));
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        // Filter Retail: Sale A (Wholesale) is excluded — its return must not subtract anything.
        var retail = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { PriceLevel = PriceLevel.Retail });
        Assert.Equal(100m, retail.SalesTaxableAmount);
        Assert.Equal(0m, retail.SalesReturnedTaxableValue);
        Assert.Equal(0m, retail.SalesReturnedGst);

        // Filter Wholesale: Sale A is included — its full-line reversal applies.
        var wholesale = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { PriceLevel = PriceLevel.Wholesale });
        Assert.Equal(100m, wholesale.SalesTaxableAmount);
        Assert.Equal(100m, wholesale.SalesReturnedTaxableValue);
        Assert.Equal(18m, wholesale.SalesReturnedGst);
        Assert.Equal(0m, wholesale.NetSalesGst);
    }

    [Fact]
    public async Task UserFilter_ReturnReversalFollowsOriginatingSaleEligibility()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var permissions = new PermissionEnforcer(db);
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), permissions);
        var returnService = new SalesReturnService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), permissions);

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customer = NewCustomer("CUST-UF", "User Filter Customer");
        var product = NewTaxableProduct("PRD-UFILTER");
        db.AddRange(customer, product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        var ownerSale = await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            CashierUserId = owner.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });
        await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            CashierUserId = cashier.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });
        var ownerSaleItem = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == ownerSale.Id);
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = ownerSale.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = ownerSaleItem.Id, Quantity = 1m }],
            ProcessedByUserId = owner.Id,
        });

        var reportService = new SalesReportService(db, permissions);
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        var filteredToCashier = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { UserId = cashier.Id });
        Assert.Equal(100m, filteredToCashier.SalesTaxableAmount);
        Assert.Equal(0m, filteredToCashier.SalesReturnedGst);

        var filteredToOwner = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { UserId = owner.Id });
        Assert.Equal(100m, filteredToOwner.SalesTaxableAmount);
        Assert.Equal(18m, filteredToOwner.SalesReturnedGst);
    }

    [Fact]
    public async Task PaymentMethodFilter_ReturnReversalFollowsOriginatingSaleEligibility()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));
        var returnService = new SalesReturnService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customer = NewCustomer("CUST-PF", "Payment Filter Customer");
        var product = NewTaxableProduct("PRD-PFILTER");
        db.AddRange(customer, product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        var cardSale = await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            CashierUserId = owner.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Card, Amount = 118m, AmountTendered = 118m }],
        });
        await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            CashierUserId = owner.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });
        var cardSaleItem = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == cardSale.Id);
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = cardSale.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = cardSaleItem.Id, Quantity = 1m }],
            ProcessedByUserId = owner.Id,
        });

        var reportService = new SalesReportService(db, new PermissionEnforcer(db));
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        var filteredToCash = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { PaymentMethod = PaymentMethod.Cash });
        Assert.Equal(100m, filteredToCash.SalesTaxableAmount);
        Assert.Equal(0m, filteredToCash.SalesReturnedGst);

        var filteredToCard = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { PaymentMethod = PaymentMethod.Card });
        Assert.Equal(100m, filteredToCard.SalesTaxableAmount);
        Assert.Equal(18m, filteredToCard.SalesReturnedGst);
    }

    [Fact]
    public async Task ProductFilter_ReturnReversalFollowsOriginatingLineEligibility()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));
        var returnService = new SalesReturnService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customer = NewCustomer("CUST-PRF", "Product Filter Customer");
        var product1 = NewTaxableProduct("PRD-PL1");
        var product2 = NewTaxableProduct("PRD-PL2");
        db.AddRange(customer, product1, product2);
        db.Inventories.AddRange(
            new Inventory { Product = product1, QuantityOnHand = 100m },
            new Inventory { Product = product2, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        var sale1 = await CompleteSaleAsync(saleService, owner.Id, customer.Id, product1.Id);
        await CompleteSaleAsync(saleService, owner.Id, customer.Id, product2.Id);
        var sale1Item = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == sale1.Id);

        // Return product 1's full line (taxable 100, GST 18).
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale1.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = sale1Item.Id, Quantity = 1m }],
            ProcessedByUserId = owner.Id,
        });

        var reportService = new SalesReportService(db, new PermissionEnforcer(db));
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        // Product 2's lines were never returned — filtering to them must show zero reversal.
        var filteredToProduct2 = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { ProductId = product2.Id });
        Assert.Equal(100m, filteredToProduct2.SalesTaxableAmount);
        Assert.Equal(0m, filteredToProduct2.SalesReturnedGst);

        // Product 1's line was returned — its full-line reversal applies where it qualifies.
        var filteredToProduct1 = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { ProductId = product1.Id });
        Assert.Equal(100m, filteredToProduct1.SalesTaxableAmount);
        Assert.Equal(18m, filteredToProduct1.SalesReturnedGst);
    }

    [Fact]
    public async Task CategoryAndBrandFilter_ReturnReversalFollowsOriginatingLineEligibility()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));
        var returnService = new SalesReturnService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var category1 = new Category { Name = "Cat One" };
        var category2 = new Category { Name = "Cat Two" };
        var brand1 = new Brand { Name = "Brand One" };
        var brand2 = new Brand { Name = "Brand Two" };
        var customer = NewCustomer("CUST-CBF", "Category Brand Customer");
        db.AddRange(category1, category2, brand1, brand2, customer);
        await db.SaveChangesAsync();

        var product1 = NewTaxableProduct("PRD-CB1");
        product1.CategoryId = category1.Id;
        product1.BrandId = brand1.Id;
        var product2 = NewTaxableProduct("PRD-CB2");
        product2.CategoryId = category2.Id;
        product2.BrandId = brand2.Id;
        db.AddRange(product1, product2);
        db.Inventories.AddRange(
            new Inventory { Product = product1, QuantityOnHand = 100m },
            new Inventory { Product = product2, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        var sale1 = await CompleteSaleAsync(saleService, owner.Id, customer.Id, product1.Id);
        await CompleteSaleAsync(saleService, owner.Id, customer.Id, product2.Id);
        var sale1Item = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == sale1.Id);
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = sale1.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = sale1Item.Id, Quantity = 1m }],
            ProcessedByUserId = owner.Id,
        });

        var reportService = new SalesReportService(db, new PermissionEnforcer(db));
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        // Excluded category/brand: no taxable contribution and zero reversal.
        var filteredToCategory2 = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { CategoryId = category2.Id });
        Assert.Equal(100m, filteredToCategory2.SalesTaxableAmount);
        Assert.Equal(0m, filteredToCategory2.SalesReturnedGst);

        var filteredToBrand2 = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { BrandId = brand2.Id });
        Assert.Equal(100m, filteredToBrand2.SalesTaxableAmount);
        Assert.Equal(0m, filteredToBrand2.SalesReturnedGst);

        // Included category/brand: the qualifying return reverses.
        var filteredToCategory1 = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { CategoryId = category1.Id });
        Assert.Equal(18m, filteredToCategory1.SalesReturnedGst);

        var filteredToBrand1 = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { BrandId = brand1.Id });
        Assert.Equal(18m, filteredToBrand1.SalesReturnedGst);
    }

    [Fact]
    public async Task DateWindows_SaleAndReturnEligibilityCombine()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var saleService = new SaleService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));
        var returnService = new SalesReturnService(db, new EfSequenceGenerator(db), new EfAuditLogger(db), new PermissionEnforcer(db));

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customer = NewCustomer("CUST-DW", "Date Window Customer");
        var product = NewTaxableProduct("PRD-DWINDOW");
        db.AddRange(customer, product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        // Sale completed today but backdated outside the report window (test-only date mutation).
        var outsideSale = await CompleteSaleAsync(saleService, owner.Id, customer.Id, product.Id);
        // Sale fully inside the window.
        var insideSale = await CompleteSaleAsync(saleService, owner.Id, customer.Id, product.Id);

        var outsideSaleItem = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == outsideSale.Id);
        var insideSaleItem = await db.SaleItems.AsNoTracking().SingleAsync(i => i.SaleId == insideSale.Id);

        // Both returns happen today (inside today's return-date window).
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = outsideSale.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = outsideSaleItem.Id, Quantity = 1m }],
            ProcessedByUserId = owner.Id,
        });
        await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
        {
            SaleId = insideSale.Id,
            Lines = [new SalesReturnLineInput { SaleItemId = insideSaleItem.Id, Quantity = 1m }],
            ProcessedByUserId = owner.Id,
        });

        // Backdate the first sale so it falls OUTSIDE the report window; its return stays today.
        var trackedOutsideSale = await db.Sales.SingleAsync(s => s.Id == outsideSale.Id);
        trackedOutsideSale.SaleDateUtc = DateTime.UtcNow.AddDays(-30);
        await db.SaveChangesAsync();

        var reportService = new SalesReportService(db, new PermissionEnforcer(db));
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        // Today's window: only the inside-qualifying sale counts, and only ITS return reverses.
        var todays = await reportService.GetGstReportAsync(range, owner.Id);
        Assert.Equal(100m, todays.SalesTaxableAmount);
        Assert.Equal(100m, todays.SalesReturnedTaxableValue);
        Assert.Equal(18m, todays.SalesReturnedGst);

        // A window over just the backdated day: the sale qualifies by date, but its return
        // (today) is outside that window → gross stands, reversal is zero.
        var backdatedRange = ReportDateRange.Resolve(
            ReportDatePreset.Custom,
            customFromLocal: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-30)),
            customToLocal: DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-29)));
        var backdated = await reportService.GetGstReportAsync(backdatedRange, owner.Id);
        Assert.Equal(100m, backdated.SalesTaxableAmount);
        Assert.Equal(0m, backdated.SalesReturnedTaxableValue);
        Assert.Equal(0m, backdated.SalesReturnedGst);
    }

    [Fact]
    public async Task LegacyRows_StayUnresolved_AndFiltersExcludeThemWithoutGuessing()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var permissions = new PermissionEnforcer(db);

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var product = NewTaxableProduct("PRD-LEGFILTER");
        db.Add(product);
        await db.SaveChangesAsync();

        // Legacy row: no identity snapshot, walk-in (no customer).
        db.Add(new Sale
        {
            InvoiceNumber = "INV-LEG-FILTER",
            SaleDateUtc = DateTime.UtcNow,
            CustomerId = null,
            GstIdentitySnapshotCapturedAtUtc = null,
            TaxableTotal = 100m,
            TaxTotal = 18m,
            GrandTotal = 118m,
            Status = SaleStatus.Completed,
            Items =
            [
                new SaleItem
                {
                    ProductId = product.Id,
                    ProductNameSnapshot = "Legacy Filter Product",
                    ProductCodeSnapshot = "PRD-LEGFILTER",
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

        var reportService = new SalesReportService(db, permissions);
        var range = ReportDateRange.Resolve(ReportDatePreset.Today);

        // Unfiltered: legacy remains unresolved — never classified from current master data.
        var unfiltered = await reportService.GetGstReportAsync(range, owner.Id);
        Assert.Equal(18m, unfiltered.SalesUnresolvedIdentityGst);
        Assert.Equal(100m, unfiltered.SalesUnresolvedJurisdictionTaxableValue);
        Assert.Equal(0m, unfiltered.SalesB2bGst);

        // Filtering to a customer excludes the legacy walk-in entirely (it has none) without
        // reclassifying or guessing anything.
        var filtered = await reportService.GetGstReportAsync(range, owner.Id, filter: new ReportFilter { CustomerId = 999999 });
        Assert.Equal(0m, filtered.SalesTaxableAmount);
        Assert.Equal(0, filtered.SalesBillCount);
        Assert.Equal(0m, filtered.SalesUnresolvedIdentityGst);
        Assert.Equal(0m, filtered.SalesUnresolvedJurisdictionTaxableValue);
    }

    private static Customer NewCustomer(string code, string name) => new()
    {
        CustomerCode = code,
        Name = name,
        StateCode = "27",
        Gstin = "27AAPFU0939F1ZV",
        GstRegistrationType = GstRegistrationType.Regular,
        IsActive = true,
    };

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
        SaleService saleService, int ownerId, int customerId, int productId, PriceLevel? priceLevel = null) =>
        saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customerId,
            CashierUserId = ownerId,
            PriceLevel = priceLevel ?? PriceLevel.Retail,
            Lines = [new SaleLineInput { ProductId = productId, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });

    public void Dispose() => _fixture.Dispose();
}
