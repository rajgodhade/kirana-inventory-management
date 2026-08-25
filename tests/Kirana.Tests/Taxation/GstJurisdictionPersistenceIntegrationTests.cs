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

/// <summary>Isolated SQLite E2E coverage from transaction completion through historical resolution.</summary>
public sealed class GstJurisdictionPersistenceIntegrationTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();

    [Fact]
    public async Task CompletedSameAndDifferentStateTransactions_KeepJurisdictionAfterAllCurrentMastersChange()
    {
        var owner = await _fixture.SeedOwnerAsync();
        await _fixture.SeedOpenRegisterAsync(owner.Id);
        var db = _fixture.Context;
        var sequence = new EfSequenceGenerator(db);
        var audit = new EfAuditLogger(db);
        var permissions = new PermissionEnforcer(db);
        var saleService = new SaleService(db, sequence, audit, permissions);
        var purchaseService = new PurchaseService(db, sequence, audit, permissions);
        var resolver = GstJurisdictionResolver.Shared;
        var contextResolver = GstTaxContextResolver.Shared;

        var store = await db.Stores.SingleAsync();
        store.IsGstEnabled = true;
        store.StateCode = "27";
        var customer = new Customer
        {
            CustomerCode = "CUST-JURISDICTION",
            Name = "Jurisdiction Customer",
            StateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            GstRegistrationType = GstRegistrationType.Regular,
            IsActive = true,
        };
        var supplier = new Supplier
        {
            SupplierCode = "SUP-JURISDICTION",
            Name = "Jurisdiction Supplier",
            StateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            GstRegistrationType = GstRegistrationType.Regular,
            IsActive = true,
        };
        var product = new Product
        {
            ProductCode = "PRD-JURISDICTION",
            Name = "Jurisdiction Product",
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

        var intraSale = await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            CashierUserId = owner.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });
        var intraPurchase = await purchaseService.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            CreatedByUserId = owner.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1m, UnitPrice = 100m }],
        });

        customer.StateCode = "29";
        customer.Gstin = "29AAACB2894G1ZJ";
        supplier.StateCode = "29";
        supplier.Gstin = "29AAACB2894G1ZJ";
        await db.SaveChangesAsync();

        var interSale = await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            CashierUserId = owner.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });
        var interPurchase = await purchaseService.FinalizePurchaseAsync(new CreatePurchaseRequest
        {
            SupplierId = supplier.Id,
            CreatedByUserId = owner.Id,
            Lines = [new PurchaseLineInput { ProductId = product.Id, Quantity = 1m, UnitPrice = 100m }],
        });

        customer.GstRegistrationType = GstRegistrationType.Unregistered;
        customer.Gstin = null;
        customer.StateCode = "27";
        await db.SaveChangesAsync();
        var intraB2CSale = await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            CashierUserId = owner.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });

        customer.StateCode = "29";
        await db.SaveChangesAsync();
        var interB2CSale = await saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            CustomerId = customer.Id,
            CashierUserId = owner.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 118m, AmountTendered = 118m }],
        });

        customer.StateCode = "27";
        customer.Gstin = null;
        customer.GstRegistrationType = GstRegistrationType.Unregistered;
        supplier.StateCode = "27";
        supplier.Gstin = null;
        supplier.GstRegistrationType = GstRegistrationType.Unregistered;
        store.StateCode = "29";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var historicalIntraSale = await db.Sales.AsNoTracking().SingleAsync(item => item.Id == intraSale.Id);
        var historicalInterSale = await db.Sales.AsNoTracking().SingleAsync(item => item.Id == interSale.Id);
        var historicalIntraB2CSale = await db.Sales.AsNoTracking().SingleAsync(item => item.Id == intraB2CSale.Id);
        var historicalInterB2CSale = await db.Sales.AsNoTracking().SingleAsync(item => item.Id == interB2CSale.Id);
        var historicalIntraPurchase = await db.Purchases.AsNoTracking().SingleAsync(item => item.Id == intraPurchase.Id);
        var historicalInterPurchase = await db.Purchases.AsNoTracking().SingleAsync(item => item.Id == interPurchase.Id);
        var saleCountBefore = await db.Sales.CountAsync();
        var purchaseCountBefore = await db.Purchases.CountAsync();
        var auditCountBefore = await db.AuditLogs.CountAsync();

        Assert.Equal(GstJurisdiction.IntraState, resolver.ResolveSale(historicalIntraSale).Jurisdiction);
        Assert.Equal(GstJurisdiction.InterState, resolver.ResolveSale(historicalInterSale).Jurisdiction);
        Assert.Equal(GstJurisdiction.IntraState, resolver.ResolvePurchase(historicalIntraPurchase).Jurisdiction);
        Assert.Equal(GstJurisdiction.InterState, resolver.ResolvePurchase(historicalInterPurchase).Jurisdiction);
        var historicalIntraSaleContext = contextResolver.ResolveSale(historicalIntraSale);
        var historicalInterSaleContext = contextResolver.ResolveSale(historicalInterSale);
        var historicalIntraB2CSaleContext = contextResolver.ResolveSale(historicalIntraB2CSale);
        var historicalInterB2CSaleContext = contextResolver.ResolveSale(historicalInterB2CSale);
        var historicalIntraPurchaseContext = contextResolver.ResolvePurchase(historicalIntraPurchase);
        var historicalInterPurchaseContext = contextResolver.ResolvePurchase(historicalInterPurchase);
        Assert.Equal(GstTransactionClass.B2B, historicalIntraSaleContext.Classification);
        Assert.Equal(GstJurisdiction.IntraState, historicalIntraSaleContext.Jurisdiction);
        Assert.Equal(GstTransactionClass.B2B, historicalInterSaleContext.Classification);
        Assert.Equal(GstJurisdiction.InterState, historicalInterSaleContext.Jurisdiction);
        Assert.Equal(GstTransactionClass.B2C, historicalIntraB2CSaleContext.Classification);
        Assert.Equal(GstJurisdiction.IntraState, historicalIntraB2CSaleContext.Jurisdiction);
        Assert.Equal(GstTransactionClass.B2C, historicalInterB2CSaleContext.Classification);
        Assert.Equal(GstJurisdiction.InterState, historicalInterB2CSaleContext.Jurisdiction);
        Assert.Equal(
            GstTransactionClass.B2B,
            contextResolver.ResolveSalesReturn(new SalesReturn { Sale = historicalIntraSale }).Classification);
        Assert.Equal(
            GstTransactionClass.B2C,
            contextResolver.ResolveSalesReturn(new SalesReturn { Sale = historicalIntraB2CSale }).Classification);
        Assert.Equal(GstPurchasePartyClass.RegisteredSupplier, historicalIntraPurchaseContext.SupplierClassification);
        Assert.Equal(GstJurisdiction.IntraState, historicalIntraPurchaseContext.Jurisdiction);
        Assert.Equal(GstPurchasePartyClass.RegisteredSupplier, historicalInterPurchaseContext.SupplierClassification);
        Assert.Equal(GstJurisdiction.InterState, historicalInterPurchaseContext.Jurisdiction);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(saleCountBefore, await db.Sales.CountAsync());
        Assert.Equal(purchaseCountBefore, await db.Purchases.CountAsync());
        Assert.Equal(auditCountBefore, await db.AuditLogs.CountAsync());

        var report = await new SalesReportService(db, permissions).GetGstReportAsync(
            ReportDateRange.Resolve(ReportDatePreset.Today), owner.Id);
        var saleBucket = Assert.Single(report.SalesByRate);
        var purchaseBucket = Assert.Single(report.PurchasesByRate);
        Assert.True(saleBucket.Cgst > 0m);
        Assert.True(saleBucket.Sgst > 0m);
        Assert.True(saleBucket.Igst > 0m);
        Assert.Equal(saleBucket.TaxAmount, saleBucket.Cgst + saleBucket.Sgst + saleBucket.Igst + saleBucket.UnresolvedGst);
        Assert.True(purchaseBucket.Cgst > 0m);
        Assert.True(purchaseBucket.Sgst > 0m);
        Assert.True(purchaseBucket.Igst > 0m);
        Assert.Equal(purchaseBucket.TaxAmount, purchaseBucket.Cgst + purchaseBucket.Sgst + purchaseBucket.Igst + purchaseBucket.UnresolvedGst);
        Assert.Equal(0m, saleBucket.UnresolvedGst);
        Assert.Equal(0m, purchaseBucket.UnresolvedGst);
    }

    public void Dispose() => _fixture.Dispose();
}
