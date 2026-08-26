using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Purchasing;
using Kirana.Application.Reports;
using Kirana.Application.Taxation;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Taxation;

/// <summary>Phase 18A-5 historical safety, end to end on isolated SQLite: after every current
/// master record is edited, completed transactions keep their jurisdiction, classification, stored
/// rates, and GST component split; returns inherit the originating transaction's context; legacy
/// rows land in explicit unresolved columns; and calculation plus reporting perform no writes.</summary>
public sealed class GstTaxCalculationHistoricalSafetyTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private static readonly GstTaxCalculator Calculator = GstTaxCalculator.Shared;
    private static readonly GstTaxContextResolver ContextResolver = GstTaxContextResolver.Shared;

    [Fact]
    public async Task MasterDataEdits_NeverChangeCompletedTransactionGst_AndReturnsInheritTheOrigin()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var sequence = new EfSequenceGenerator(db);
        var audit = new EfAuditLogger(db);
        var permissions = new PermissionEnforcer(db);
        var saleService = new SaleService(db, sequence, audit, permissions);
        var purchaseService = new PurchaseService(db, sequence, audit, permissions);

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customer = new Customer
        {
            CustomerCode = "CUST-CALC",
            Name = "Calc Customer",
            StateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            GstRegistrationType = GstRegistrationType.Regular,
            IsActive = true,
        };
        var supplier = new Supplier
        {
            SupplierCode = "SUP-CALC",
            Name = "Calc Supplier",
            StateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            GstRegistrationType = GstRegistrationType.Regular,
            IsActive = true,
        };
        var product = new Product
        {
            ProductCode = "PRD-CALC",
            Name = "Calc Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 100m,
            Mrp = 130m,
            SellingPrice = 100m,
            GstRatePercent = 18m,
            IsTaxInclusive = false,
            IsActive = true,
        }.WithRetailPrice();
        db.AddRange(customer, supplier, product);
        db.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        await db.SaveChangesAsync();

        var sale = await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            CashierUserId = owner.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });
        var purchase = await purchaseService.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            CreatedByUserId = owner.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1m, UnitPrice = 100m }],
        });

        // FI-3/FI-4 bait: change EVERY current input the wrong implementation could reach for —
        // store, customer, and supplier identity plus today's product GST rate.
        customer.StateCode = "29";
        customer.Gstin = "29AAACB2894G1ZJ";
        supplier.StateCode = "29";
        supplier.Gstin = "29AAACB2894G1ZJ";
        store.StateCode = "29";
        product.GstRatePercent = 28m;
        customer.GstRegistrationType = GstRegistrationType.Unregistered;
        supplier.GstRegistrationType = GstRegistrationType.Unregistered;
        store.GstRegistrationType = GstRegistrationType.Unregistered;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var saleCountBefore = await db.Sales.CountAsync();
        var purchaseCountBefore = await db.Purchases.CountAsync();
        var auditCountBefore = await db.AuditLogs.CountAsync();
        var fingerprintBefore = await DataFingerprint(db);

        // Current-master navigations are deliberately loaded: even with today's edited records in
        // memory, every GST decision below must come from the transaction snapshots alone.
        var historicalSale = await db.Sales.AsNoTracking()
            .Include(i => i.Items)
            .Include(s => s.Customer)
            .SingleAsync(item => item.Id == sale.Id);
        var historicalPurchase = await db.Purchases.AsNoTracking()
            .Include(i => i.Items)
            .Include(p => p.Supplier)
            .SingleAsync(item => item.Id == purchase.Id);

        // Steps 8-9: sale/purchase GST is derived only from the historical snapshots.
        var saleContext = ContextResolver.ResolveSale(historicalSale);
        Assert.Equal(GstJurisdiction.IntraState, saleContext.Jurisdiction);
        Assert.Equal(GstTransactionClass.B2B, saleContext.Classification);
        var saleItem = Assert.Single(historicalSale.Items);
        Assert.Equal(100m, saleItem.TaxableAmount);
        Assert.Equal(18m, saleItem.GstAmount);
        Assert.Equal(18m, saleItem.GstRatePercentSnapshot);

        var saleSplit = Calculator.SplitStored(saleContext, saleItem.TaxableAmount, saleItem.GstAmount);
        Assert.True(saleSplit.IsResolved);
        Assert.Equal(GstJurisdiction.IntraState, saleSplit.Jurisdiction);
        Assert.Equal(9m, saleSplit.Cgst);
        Assert.Equal(9m, saleSplit.Sgst);
        Assert.Equal(0m, saleSplit.Igst);

        // The snapshot rate still reproduces the stored GST exactly — never today's 28%.
        var recalculated = Calculator.Calculate(saleContext, saleItem.TaxableAmount, saleItem.GstRatePercentSnapshot);
        Assert.Equal(saleItem.GstAmount, recalculated.TotalGst);

        var purchaseContext = ContextResolver.ResolvePurchase(historicalPurchase);
        Assert.Equal(GstJurisdiction.IntraState, purchaseContext.Jurisdiction);
        Assert.Equal(GstPurchasePartyClass.RegisteredSupplier, purchaseContext.SupplierClassification);
        var purchaseItem = Assert.Single(historicalPurchase.Items);
        Assert.Equal(100m, purchaseItem.TaxableAmount);
        Assert.Equal(18m, purchaseItem.GstAmount);

        var purchaseSplit = Calculator.SplitStored(purchaseContext, purchaseItem.TaxableAmount, purchaseItem.GstAmount);
        Assert.True(purchaseSplit.IsResolved);
        Assert.Equal(9m, purchaseSplit.Cgst);
        Assert.Equal(9m, purchaseSplit.Sgst);
        Assert.Equal(0m, purchaseSplit.Igst);

        // Step 10: returns reuse the originating transaction's context and stored values.
        var salesReturnContext = ContextResolver.ResolveSalesReturn(new SalesReturn { Sale = historicalSale });
        Assert.Equal(GstJurisdiction.IntraState, salesReturnContext.Jurisdiction);
        Assert.Equal(GstTransactionClass.B2B, salesReturnContext.Classification);
        var salesReturnSplit = Calculator.SplitStored(salesReturnContext, saleItem.TaxableAmount, saleItem.GstAmount);
        Assert.Equal(9m, salesReturnSplit.Cgst);
        Assert.Equal(9m, salesReturnSplit.Sgst);
        Assert.Equal(0m, salesReturnSplit.Igst);

        var purchaseReturnContext = ContextResolver.ResolvePurchaseReturn(new PurchaseReturn { Purchase = historicalPurchase });
        Assert.Equal(GstJurisdiction.IntraState, purchaseReturnContext.Jurisdiction);
        Assert.Equal(GstPurchasePartyClass.RegisteredSupplier, purchaseReturnContext.SupplierClassification);

        // Step 14: calculation performed no writes; transaction counts are untouched.
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(saleCountBefore, await db.Sales.CountAsync());
        Assert.Equal(purchaseCountBefore, await db.Purchases.CountAsync());
        Assert.Equal(auditCountBefore, await db.AuditLogs.CountAsync());

        // Step 16: report reconciliation after the master-data edits.
        var report = await new SalesReportService(db, permissions).GetGstReportAsync(
            ReportDateRange.Resolve(ReportDatePreset.Today), owner.Id);
        var saleBucket = Assert.Single(report.SalesByRate);
        var purchaseBucket = Assert.Single(report.PurchasesByRate);

        Assert.Equal(100m, saleBucket.TaxableAmount);
        Assert.Equal(18m, saleBucket.TaxAmount);
        // The report must bucket by the transaction's STORED rate — today's product rate (28%
        // after the edit above) must not move or relabel the historical slab.
        Assert.Equal(18m, saleBucket.RatePercent);
        Assert.Equal(9m, saleBucket.Cgst);
        Assert.Equal(9m, saleBucket.Sgst);
        Assert.Equal(0m, saleBucket.Igst);
        Assert.Equal(0m, saleBucket.UnresolvedGst);
        Assert.Equal(saleBucket.TaxAmount, saleBucket.Cgst + saleBucket.Sgst + saleBucket.Igst + saleBucket.UnresolvedGst);
        // Exclusive pricing model: taxable + GST == gross tax-inclusive line value.
        Assert.Equal(118m, saleBucket.TaxableAmount + saleBucket.TaxAmount);

        Assert.Equal(100m, purchaseBucket.TaxableAmount);
        Assert.Equal(18m, purchaseBucket.TaxAmount);
        Assert.Equal(9m, purchaseBucket.Cgst);
        Assert.Equal(9m, purchaseBucket.Sgst);
        Assert.Equal(0m, purchaseBucket.Igst);
        Assert.Equal(purchaseBucket.TaxAmount, purchaseBucket.Cgst + purchaseBucket.Sgst + purchaseBucket.Igst + purchaseBucket.UnresolvedGst);

        // Classification totals reconcile against the same stored GST.
        Assert.Equal(18m, report.SalesB2bGst);
        Assert.Equal(0m, report.SalesB2cGst);
        Assert.Equal(0m, report.SalesUnresolvedIdentityGst);
        Assert.Equal(report.SalesGstCollected, report.SalesB2bGst + report.SalesB2cGst + report.SalesUnresolvedIdentityGst);
        Assert.Equal(18m, report.PurchaseRegisteredSupplierGst);
        Assert.Equal(0m, report.PurchaseUnregisteredSupplierGst);
        Assert.Equal(0m, report.PurchaseUnresolvedSupplierGst);
        Assert.Equal(report.PurchaseGstPaid, report.PurchaseRegisteredSupplierGst + report.PurchaseUnregisteredSupplierGst + report.PurchaseUnresolvedSupplierGst);

        // Step 14: the whole calculate-and-report pass left no monetary trace behind.
        Assert.Equal(fingerprintBefore, await DataFingerprint(db));
    }

    /// <summary>Monetary fingerprint over every GST-relevant column. Any write performed by the
    /// calculation or reporting path changes at least one term.</summary>
    private static async Task<(decimal SalesTax, decimal SalesGrand, decimal SalesRoundOff, decimal ItemGst, decimal PurchaseItemGst, decimal ItemTaxable)>
        DataFingerprint(KiranaDbContext db)
    {
        var sales = await db.Sales.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Tax = g.Sum(x => x.TaxTotal),
                Grand = g.Sum(x => x.GrandTotal),
                RoundOff = g.Sum(x => x.RoundOffAmount),
            })
            .SingleAsync();
        var itemGst = await db.SaleItems.AsNoTracking().SumAsync(i => (decimal?)i.GstAmount) ?? 0m;
        var itemTaxable = await db.SaleItems.AsNoTracking().SumAsync(i => (decimal?)i.TaxableAmount) ?? 0m;
        var purchaseItemGst = await db.PurchaseItems.AsNoTracking().SumAsync(i => (decimal?)i.GstAmount) ?? 0m;
        return (sales.Tax, sales.Grand, sales.RoundOff, itemGst, purchaseItemGst, itemTaxable);
    }

    [Fact]
    public async Task LegacySaleRows_LandInExplicitUnresolvedColumns_WithoutGuessing()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var permissions = new PermissionEnforcer(db);

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var product = new Product
        {
            ProductCode = "PRD-LEGACY",
            Name = "Legacy Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 100m,
            Mrp = 130m,
            SellingPrice = 100m,
            GstRatePercent = 18m,
            IsTaxInclusive = false,
            IsActive = true,
        }.WithRetailPrice();
        db.Add(product);
        await db.SaveChangesAsync();

        // A pre-18A-2 row: GST amounts exist but no identity capture marker. Its stored tax must be
        // reported as unresolved, never split as intra-state or inter-state by assumption.
        db.Add(new Sale
        {
            InvoiceNumber = "INV-LEGACY",
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
                    ProductNameSnapshot = "Legacy Product",
                    ProductCodeSnapshot = "PRD-LEGACY",
                    UnitSnapshot = "PCS",
                    IsTaxInclusiveSnapshot = false,
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

        var legacySale = await db.Sales.AsNoTracking().Include(i => i.Items).SingleAsync(item => item.InvoiceNumber == "INV-LEGACY");
        var context = ContextResolver.ResolveSale(legacySale);
        Assert.False(context.JurisdictionResolution.IsResolved);
        Assert.Equal(GstJurisdictionUnresolvedReason.LegacyTransaction, context.JurisdictionResolution.UnresolvedReason);

        var line = Assert.Single(legacySale.Items);
        var split = Calculator.SplitStored(context, line.TaxableAmount, line.GstAmount);
        Assert.False(split.IsResolved);
        Assert.All([split.Cgst, split.Sgst, split.Igst], amount => Assert.Equal(0m, amount));

        var report = await new SalesReportService(db, permissions).GetGstReportAsync(
            ReportDateRange.Resolve(ReportDatePreset.Today), owner.Id);
        var bucket = Assert.Single(report.SalesByRate);
        Assert.Equal(18m, bucket.TaxAmount);
        Assert.Equal(18m, bucket.UnresolvedGst);
        Assert.Equal(0m, bucket.Cgst);
        Assert.Equal(0m, bucket.Sgst);
        Assert.Equal(0m, bucket.Igst);
        Assert.Equal(bucket.TaxAmount, bucket.Cgst + bucket.Sgst + bucket.Igst + bucket.UnresolvedGst);
        Assert.Equal(18m, report.SalesUnresolvedIdentityGst);
        Assert.Equal(0m, report.SalesB2bGst);
        Assert.Equal(0m, report.SalesB2cGst);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    public void Dispose() => _fixture.Dispose();
}
