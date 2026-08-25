using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Kirana.Application.Taxation;

namespace Kirana.Application.Reports;

public sealed class SalesReportService(
    IKiranaDbContext db,
    IPermissionEnforcer permissionEnforcer,
    IGstCalculationService? gstCalculationService = null,
    IGstTaxContextResolver? gstTaxContextResolver = null,
    IGstTaxCalculator? gstTaxCalculator = null) : ISalesReportService
{
    public async Task<SalesReportSummary> GetSummaryAsync(
        ReportDateRange range, ReportFilter? filter, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var sales = FilterSales(BaseSalesQuery(range), filter);

        var grossSales = await sales.SumAsync(s => (decimal?)s.GrandTotal, cancellationToken) ?? 0m;
        var billDiscounts = await sales.SumAsync(s => (decimal?)s.BillDiscountAmount, cancellationToken) ?? 0m;
        var gstCollected = await sales.SumAsync(s => (decimal?)s.TaxTotal, cancellationToken) ?? 0m;
        var billCount = await sales.CountAsync(cancellationToken);

        var items = FilterSaleItems(BaseSaleItemsQuery(range), filter);
        var itemsSold = await items.SumAsync(i => (decimal?)i.Quantity, cancellationToken) ?? 0m;
        var itemDiscounts = await items.SumAsync(i => (decimal?)i.DiscountAmount, cancellationToken) ?? 0m;

        // Returns are dated by when the return happened, not the original sale — a return made
        // today against last month's invoice reduces today's net figure, matching how a
        // shopkeeper reconciles the till at day's end.
        var returns = await db.SalesReturns.AsNoTracking()
            .Where(r => r.ReturnDateUtc >= range.StartUtc && r.ReturnDateUtc < range.EndUtc)
            .SumAsync(r => (decimal?)r.TotalReturnAmount, cancellationToken) ?? 0m;

        // One grouped query rather than two filtered sums, so the split cannot drift from the
        // gross figure above and costs a single round trip.
        var byLevel = await sales
            .GroupBy(s => s.PriceLevel)
            .Select(g => new { Level = g.Key, Total = g.Sum(x => x.GrandTotal), Count = g.Count() })
            .ToListAsync(cancellationToken);

        var retail = byLevel.FirstOrDefault(x => x.Level == PriceLevel.Retail);
        var wholesale = byLevel.FirstOrDefault(x => x.Level == PriceLevel.Wholesale);

        var payments = await FilterPayments(BasePaymentsQuery(range), filter)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new SalesReportSummary
        {
            Range = range,
            GrossSales = grossSales,
            Returns = returns,
            NetSales = grossSales - returns,
            ItemDiscounts = itemDiscounts,
            BillDiscounts = billDiscounts,
            TotalDiscounts = itemDiscounts + billDiscounts,
            GstCollected = gstCollected,
            BillCount = billCount,
            AverageBillValue = billCount == 0 ? 0m : Math.Round(grossSales / billCount, 2),
            ItemsSold = itemsSold,
            RetailSales = retail?.Total ?? 0m,
            WholesaleSales = wholesale?.Total ?? 0m,
            RetailBillCount = retail?.Count ?? 0,
            WholesaleBillCount = wholesale?.Count ?? 0,
            PaymentMethodBreakdown = payments
                .Select(p => new PaymentMethodAmount { Method = ReportFormatting.FormatPaymentMethod(p.Method), Amount = p.Total, Count = p.Count })
                .OrderByDescending(p => p.Amount)
                .ToList(),
        };
    }

    public async Task<GstReport> GetGstReportAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var saleItems = await db.SaleItems.AsNoTracking()
            .Include(i => i.Sale)
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc)
            .ToListAsync(cancellationToken);

        var purchaseItems = await db.PurchaseItems.AsNoTracking()
            .Include(i => i.Purchase)
            .Where(i => i.Purchase.PurchaseDateUtc >= range.StartUtc && i.Purchase.PurchaseDateUtc < range.EndUtc)
            .ToListAsync(cancellationToken);

        var calculator = gstCalculationService ?? GstCalculationService.Shared;
        var contextResolver = gstTaxContextResolver ?? GstTaxContextResolver.Shared;
        var taxCalculator = gstTaxCalculator ?? GstTaxCalculator.Shared;

        // Phase 18A-5: one explicit tax context per transaction. Classification (Phase 18A-4) and
        // jurisdiction (Phase 18A-3) both come from the historical snapshots through the shared
        // resolvers, and the component split itself is delegated to IGstTaxCalculator. Stored GST
        // amounts stay authoritative; they are only ever split, never recomputed.
        var saleLines = saleItems
            .Select(i => new ResolvedGstSnapshotLine(ToSnapshot(i), contextResolver.ResolveSale(i.Sale)))
            .ToList();
        var purchaseLines = purchaseItems
            .Select(i => new ResolvedPurchaseGstSnapshotLine(ToSnapshot(i), contextResolver.ResolvePurchase(i.Purchase)))
            .ToList();
        var salesBreakdown = BuildJurisdictionBreakdown(
            saleLines.Select(line => (line.Snapshot, line.Context.JurisdictionResolution)).ToList(),
            calculator, taxCalculator);
        var purchaseBreakdown = BuildJurisdictionBreakdown(
            purchaseLines.Select(line => (line.Snapshot, line.Context.JurisdictionResolution)).ToList(),
            calculator, taxCalculator);

        return new GstReport
        {
            Range = range,
            SalesTaxableAmount = salesBreakdown.Sum(b => b.TaxableAmount),
            SalesGstCollected = salesBreakdown.Sum(b => b.TaxAmount),
            SalesByRate = salesBreakdown,
            SalesB2bGst = SumSaleClassGst(saleLines, GstTransactionClass.B2B),
            SalesB2cGst = SumSaleClassGst(saleLines, GstTransactionClass.B2C),
            SalesUnresolvedIdentityGst = SumSaleClassGst(saleLines, GstTransactionClass.Unresolved),
            PurchaseTaxableAmount = purchaseBreakdown.Sum(b => b.TaxableAmount),
            PurchaseGstPaid = purchaseBreakdown.Sum(b => b.TaxAmount),
            PurchasesByRate = purchaseBreakdown,
            PurchaseRegisteredSupplierGst = SumPurchaseClassGst(purchaseLines, GstPurchasePartyClass.RegisteredSupplier),
            PurchaseUnregisteredSupplierGst = SumPurchaseClassGst(purchaseLines, GstPurchasePartyClass.UnregisteredSupplier),
            PurchaseUnresolvedSupplierGst = SumPurchaseClassGst(purchaseLines, GstPurchasePartyClass.Unresolved),
        };
    }

    private static decimal SumSaleClassGst(IReadOnlyList<ResolvedGstSnapshotLine> lines, GstTransactionClass classification) =>
        lines.Where(line => line.Context.Classification == classification).Sum(line => line.Snapshot.GstAmount);

    private static decimal SumPurchaseClassGst(IReadOnlyList<ResolvedPurchaseGstSnapshotLine> lines, GstPurchasePartyClass classification) =>
        lines.Where(line => line.Context.SupplierClassification == classification).Sum(line => line.Snapshot.GstAmount);

    private static IReadOnlyList<GstRateBreakdown> BuildJurisdictionBreakdown(
        IReadOnlyList<(GstSnapshotLine Snapshot, GstJurisdictionResolution Jurisdiction)> lines,
        IGstCalculationService calculator,
        IGstTaxCalculator taxCalculator)
    {
        var jurisdictionSlabs = lines
            .GroupBy(line => line.Jurisdiction.Jurisdiction)
            .SelectMany(group =>
            {
                // Every line in a group shares the same resolved jurisdiction; the representative
                // resolution carries it into the centralized split. The split depends only on the
                // jurisdiction outcome, never on which transaction was picked.
                var representative = group.First().Jurisdiction;
                return calculator.SummarizeStored(group.Select(line => line.Snapshot).ToList())
                    .Select(slab => ToJurisdictionBreakdown(slab, representative, taxCalculator));
            });

        return jurisdictionSlabs
            .GroupBy(row => new { row.RatePercent, row.PricingType })
            .Select(group => new GstRateBreakdown
            {
                RatePercent = group.Key.RatePercent,
                PricingType = group.Key.PricingType,
                TaxableAmount = group.Sum(row => row.TaxableAmount),
                TaxAmount = group.Sum(row => row.TaxAmount),
                Cgst = group.Sum(row => row.Cgst),
                Sgst = group.Sum(row => row.Sgst),
                Igst = group.Sum(row => row.Igst),
                UnresolvedGst = group.Sum(row => row.UnresolvedGst),
                InvoiceCount = group.Sum(row => row.InvoiceCount),
            })
            .OrderBy(row => row.RatePercent)
            .ThenBy(row => row.PricingType)
            .ToList();
    }

    private static GstRateBreakdown ToJurisdictionBreakdown(
        GstSlabSummary slab, GstJurisdictionResolution jurisdiction, IGstTaxCalculator taxCalculator)
    {
        // Stored amounts stay authoritative; IGstTaxCalculator only allocates the components.
        var split = taxCalculator.SplitStored(jurisdiction, slab.TaxableAmount, slab.GstAmount);
        return new GstRateBreakdown
        {
            RatePercent = slab.RatePercent,
            TaxableAmount = slab.TaxableAmount,
            TaxAmount = slab.GstAmount,
            InvoiceCount = slab.InvoiceCount,
            PricingType = slab.PricingType,
            Cgst = split.Cgst,
            Sgst = split.Sgst,
            Igst = split.Igst,
            UnresolvedGst = split.IsResolved ? 0m : slab.GstAmount,
        };
    }

    private static GstSnapshotLine ToSnapshot(SaleItem item) => new()
    {
        TransactionId = item.SaleId,
        RatePercent = item.GstRatePercentSnapshot,
        TaxableAmount = item.TaxableAmount,
        GstAmount = item.GstAmount,
        PricingType = item.IsTaxInclusiveSnapshot ? PricingType.Inclusive : PricingType.Exclusive,
    };

    private static GstSnapshotLine ToSnapshot(PurchaseItem item) => new()
    {
        TransactionId = item.PurchaseId,
        RatePercent = item.GstRatePercentSnapshot,
        TaxableAmount = item.TaxableAmount,
        GstAmount = item.GstAmount,
        PricingType = item.IsTaxInclusiveSnapshot ? PricingType.Inclusive : PricingType.Exclusive,
    };

    private sealed record ResolvedGstSnapshotLine(GstSnapshotLine Snapshot, GstTaxContext Context);

    private sealed record ResolvedPurchaseGstSnapshotLine(GstSnapshotLine Snapshot, GstPurchaseTaxContext Context);

    // ------------------------------------------------------------ shared filtered queries

    private IQueryable<Sale> BaseSalesQuery(ReportDateRange range) =>
        db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= range.StartUtc && s.SaleDateUtc < range.EndUtc);

    private IQueryable<SaleItem> BaseSaleItemsQuery(ReportDateRange range) =>
        db.SaleItems.AsNoTracking().Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc);

    private IQueryable<Payment> BasePaymentsQuery(ReportDateRange range) =>
        db.Payments.AsNoTracking().Where(p => p.Sale.Status == SaleStatus.Completed && p.Sale.SaleDateUtc >= range.StartUtc && p.Sale.SaleDateUtc < range.EndUtc);

    private static IQueryable<Sale> FilterSales(IQueryable<Sale> sales, ReportFilter? filter)
    {
        if (filter is null)
        {
            return sales;
        }

        if (filter.CustomerId is { } customerId)
        {
            sales = sales.Where(s => s.CustomerId == customerId);
        }

        if (filter.UserId is { } userId)
        {
            sales = sales.Where(s => s.CashierUserId == userId);
        }

        if (filter.PaymentMethod is { } method)
        {
            sales = sales.Where(s => s.Payments.Any(p => p.Method == method));
        }

        // The level stored ON the sale. Reconstructing it from today's ProductPrice would
        // reclassify old bills every time a price moved.
        if (filter.PriceLevel is { } priceLevel)
        {
            sales = sales.Where(s => s.PriceLevel == priceLevel);
        }

        if (filter.ProductId is { } productId)
        {
            sales = sales.Where(s => s.Items.Any(i => i.ProductId == productId));
        }

        if (filter.CategoryId is { } categoryId)
        {
            sales = sales.Where(s => s.Items.Any(i => i.Product.CategoryId == categoryId));
        }

        if (filter.BrandId is { } brandId)
        {
            sales = sales.Where(s => s.Items.Any(i => i.Product.BrandId == brandId));
        }

        return sales;
    }

    private static IQueryable<SaleItem> FilterSaleItems(IQueryable<SaleItem> items, ReportFilter? filter)
    {
        if (filter is null)
        {
            return items;
        }

        if (filter.CustomerId is { } customerId)
        {
            items = items.Where(i => i.Sale.CustomerId == customerId);
        }

        if (filter.UserId is { } userId)
        {
            items = items.Where(i => i.Sale.CashierUserId == userId);
        }

        if (filter.PaymentMethod is { } method)
        {
            items = items.Where(i => i.Sale.Payments.Any(p => p.Method == method));
        }

        if (filter.ProductId is { } productId)
        {
            items = items.Where(i => i.ProductId == productId);
        }

        if (filter.CategoryId is { } categoryId)
        {
            items = items.Where(i => i.Product.CategoryId == categoryId);
        }

        if (filter.BrandId is { } brandId)
        {
            items = items.Where(i => i.Product.BrandId == brandId);
        }

        return items;
    }

    private static IQueryable<Payment> FilterPayments(IQueryable<Payment> payments, ReportFilter? filter)
    {
        if (filter is null)
        {
            return payments;
        }

        if (filter.CustomerId is { } customerId)
        {
            payments = payments.Where(p => p.Sale.CustomerId == customerId);
        }

        if (filter.UserId is { } userId)
        {
            payments = payments.Where(p => p.Sale.CashierUserId == userId);
        }

        if (filter.PaymentMethod is { } method)
        {
            payments = payments.Where(p => p.Method == method);
        }

        if (filter.ProductId is { } productId)
        {
            payments = payments.Where(p => p.Sale.Items.Any(i => i.ProductId == productId));
        }

        if (filter.CategoryId is { } categoryId)
        {
            payments = payments.Where(p => p.Sale.Items.Any(i => i.Product.CategoryId == categoryId));
        }

        if (filter.BrandId is { } brandId)
        {
            payments = payments.Where(p => p.Sale.Items.Any(i => i.Product.BrandId == brandId));
        }

        return payments;
    }
}
