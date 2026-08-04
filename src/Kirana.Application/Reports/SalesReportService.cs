using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Reports;

public sealed class SalesReportService(IKiranaDbContext db, IPermissionEnforcer permissionEnforcer) : ISalesReportService
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

        var salesByRate = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc)
            .GroupBy(i => i.GstRatePercentSnapshot)
            .Select(g => new { Rate = g.Key, Taxable = g.Sum(x => x.TaxableAmount), Tax = g.Sum(x => x.GstAmount) })
            .OrderBy(g => g.Rate)
            .ToListAsync(cancellationToken);

        var purchasesByRate = await db.PurchaseItems.AsNoTracking()
            .Where(i => i.Purchase.PurchaseDateUtc >= range.StartUtc && i.Purchase.PurchaseDateUtc < range.EndUtc)
            .GroupBy(i => i.GstRatePercentSnapshot)
            .Select(g => new { Rate = g.Key, Taxable = g.Sum(x => x.TaxableAmount), Tax = g.Sum(x => x.GstAmount) })
            .OrderBy(g => g.Rate)
            .ToListAsync(cancellationToken);

        static GstRateBreakdown ToBreakdown(decimal rate, decimal taxable, decimal tax) => new()
        {
            RatePercent = rate,
            TaxableAmount = taxable,
            TaxAmount = tax,
            // Split evenly assuming intra-state — see the GstReport class doc for why.
            Cgst = Math.Round(tax / 2m, 2),
            Sgst = tax - Math.Round(tax / 2m, 2),
            Igst = 0m,
        };

        var salesBreakdown = salesByRate.Select(r => ToBreakdown(r.Rate, r.Taxable, r.Tax)).ToList();
        var purchaseBreakdown = purchasesByRate.Select(r => ToBreakdown(r.Rate, r.Taxable, r.Tax)).ToList();

        return new GstReport
        {
            Range = range,
            SalesTaxableAmount = salesBreakdown.Sum(b => b.TaxableAmount),
            SalesGstCollected = salesBreakdown.Sum(b => b.TaxAmount),
            SalesByRate = salesBreakdown,
            PurchaseTaxableAmount = purchaseBreakdown.Sum(b => b.TaxableAmount),
            PurchaseGstPaid = purchaseBreakdown.Sum(b => b.TaxAmount),
            PurchasesByRate = purchaseBreakdown,
        };
    }

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
