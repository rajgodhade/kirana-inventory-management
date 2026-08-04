using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Reports;

public sealed class DashboardService(
    IKiranaDbContext db,
    IInventoryService inventoryService,
    IProfitReportService profitReportService,
    IPermissionEnforcer permissionEnforcer) : IDashboardService
{
    public async Task<DashboardSummary> GetSummaryAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var totalSales = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= range.StartUtc && s.SaleDateUtc < range.EndUtc)
            .SumAsync(s => (decimal?)s.GrandTotal, cancellationToken) ?? 0m;

        var billCount = await db.Sales.AsNoTracking()
            .CountAsync(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= range.StartUtc && s.SaleDateUtc < range.EndUtc, cancellationToken);

        var itemsSold = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc)
            .SumAsync(i => (decimal?)i.Quantity, cancellationToken) ?? 0m;

        var totalPurchases = await db.Purchases.AsNoTracking()
            .Where(p => p.PurchaseDateUtc >= range.StartUtc && p.PurchaseDateUtc < range.EndUtc)
            .SumAsync(p => (decimal?)p.GrandTotal, cancellationToken) ?? 0m;

        var totalExpenses = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDateUtc >= range.StartUtc && e.ExpenseDateUtc < range.EndUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        // Point-in-time figures — deliberately NOT filtered by range (see DashboardSummary docs).
        var inventoryValue = await db.Inventories.AsNoTracking()
            .SumAsync(i => (decimal?)(i.QuantityOnHand * i.Product.PurchasePrice), cancellationToken) ?? 0m;

        var lowStockCount = (await inventoryService.GetLowStockProductsAsync(cancellationToken)).Count;
        var outOfStockCount = (await inventoryService.GetOutOfStockProductsAsync(cancellationToken)).Count;

        var customerOutstanding = await db.CustomerCredits.AsNoTracking()
            .Where(c => c.RemainingAmount > 0)
            .SumAsync(c => (decimal?)c.RemainingAmount, cancellationToken) ?? 0m;

        var supplierOutstanding = await db.Suppliers.AsNoTracking()
            .Where(s => s.OutstandingBalance > 0)
            .SumAsync(s => (decimal?)s.OutstandingBalance, cancellationToken) ?? 0m;

        var canViewProfit = await permissionEnforcer.HasPermissionAsync(performedByUserId, PermissionKeys.ReportsViewProfit, cancellationToken);
        decimal? grossProfit = null;
        decimal? netProfit = null;
        if (canViewProfit)
        {
            var profit = await profitReportService.GetSummaryAsync(range, performedByUserId, cancellationToken);
            grossProfit = profit.GrossProfit;
            netProfit = profit.NetProfit;
        }

        return new DashboardSummary
        {
            Range = range,
            TotalSales = totalSales,
            TotalPurchases = totalPurchases,
            TotalExpenses = totalExpenses,
            GrossProfit = grossProfit,
            NetProfit = netProfit,
            BillCount = billCount,
            ItemsSold = itemsSold,
            InventoryValue = inventoryValue,
            LowStockCount = lowStockCount,
            OutOfStockCount = outOfStockCount,
            CustomerOutstanding = customerOutstanding,
            SupplierOutstanding = supplierOutstanding,
            CanViewProfit = canViewProfit,
        };
    }

    public async Task<DashboardCharts> GetChartsAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var nowLocal = DateTime.Now;
        var canViewProfit = await permissionEnforcer.HasPermissionAsync(performedByUserId, PermissionKeys.ReportsViewProfit, cancellationToken);

        var dailySales = await BuildDailySalesTrendAsync(nowLocal, cancellationToken);
        var weeklySales = await BuildWeeklySalesAsync(nowLocal, cancellationToken);
        var monthlySales = await BuildMonthlySalesAsync(nowLocal, cancellationToken);
        var (salesVsExpensesSales, salesVsExpensesExpenses) = await BuildSalesVsExpensesAsync(nowLocal, cancellationToken);
        var grossProfitTrend = canViewProfit
            ? await BuildGrossProfitTrendAsync(nowLocal, cancellationToken)
            : new ChartSeries { Name = "Gross Profit", Points = [] };

        var categorySales = await BuildProductCategorySalesAsync(range, cancellationToken);
        var paymentMethods = await BuildPaymentMethodDistributionAsync(range, cancellationToken);

        var topCustomers = await GetTopCustomersAsync(range, performedByUserId, take: 5, cancellationToken);
        var topSuppliers = await GetTopSuppliersAsync(range, performedByUserId, take: 5, cancellationToken);

        return new DashboardCharts
        {
            DailySalesTrend = dailySales,
            WeeklySales = weeklySales,
            MonthlySales = monthlySales,
            SalesVsExpensesSales = salesVsExpensesSales,
            SalesVsExpensesExpenses = salesVsExpensesExpenses,
            GrossProfitTrend = grossProfitTrend,
            ProductCategorySales = categorySales,
            PaymentMethodDistribution = paymentMethods,
            TopCustomers = new ChartSeries
            {
                Name = "Top Customers",
                Points = topCustomers.Select(c => new ChartPoint { Label = c.Name, Value = c.Amount }).ToList(),
            },
            TopSuppliers = new ChartSeries
            {
                Name = "Top Suppliers",
                Points = topSuppliers.Select(s => new ChartPoint { Label = s.Name, Value = s.Amount }).ToList(),
            },
        };
    }

    public async Task<IReadOnlyList<RecentSaleRow>> GetRecentSalesAsync(
        int? performedByUserId, int take = 6, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        return await db.Sales.AsNoTracking()
            .OrderByDescending(s => s.SaleDateUtc)
            .Take(take)
            .Select(s => new RecentSaleRow
            {
                SaleId = s.Id,
                InvoiceNumber = s.InvoiceNumber,
                SaleDateUtc = s.SaleDateUtc,
                CustomerName = s.Customer != null ? s.Customer.Name : null,
                GrandTotal = s.GrandTotal,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecentPurchaseRow>> GetRecentPurchasesAsync(
        int? performedByUserId, int take = 6, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        return await db.Purchases.AsNoTracking()
            .OrderByDescending(p => p.PurchaseDateUtc)
            .Take(take)
            .Select(p => new RecentPurchaseRow
            {
                PurchaseId = p.Id,
                PurchaseNumber = p.PurchaseNumber,
                PurchaseDateUtc = p.PurchaseDateUtc,
                SupplierName = p.Supplier.Name,
                GrandTotal = p.GrandTotal,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RecentReturnRow>> GetRecentReturnsAsync(
        int? performedByUserId, int take = 6, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var salesReturns = await db.SalesReturns.AsNoTracking()
            .OrderByDescending(r => r.ReturnDateUtc)
            .Take(take)
            .Select(r => new RecentReturnRow
            {
                ReturnNumber = r.ReturnNumber,
                ReferenceNumber = r.InvoiceNumberSnapshot,
                ReturnDateUtc = r.ReturnDateUtc,
                Amount = r.TotalReturnAmount,
                IsPurchaseReturn = false,
            })
            .ToListAsync(cancellationToken);

        var purchaseReturns = await db.PurchaseReturns.AsNoTracking()
            .OrderByDescending(r => r.ReturnDateUtc)
            .Take(take)
            .Select(r => new RecentReturnRow
            {
                ReturnNumber = r.ReturnNumber,
                ReferenceNumber = r.PurchaseNumberSnapshot,
                ReturnDateUtc = r.ReturnDateUtc,
                Amount = r.TotalReturnAmount,
                IsPurchaseReturn = true,
            })
            .ToListAsync(cancellationToken);

        return salesReturns.Concat(purchaseReturns)
            .OrderByDescending(r => r.ReturnDateUtc)
            .Take(take)
            .ToList();
    }

    public async Task<IReadOnlyList<RecentExpenseRow>> GetRecentExpensesAsync(
        int? performedByUserId, int take = 6, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        return await db.Expenses.AsNoTracking()
            .OrderByDescending(e => e.ExpenseDateUtc)
            .Take(take)
            .Select(e => new RecentExpenseRow
            {
                ExpenseId = e.Id,
                ExpenseNumber = e.ExpenseNumber,
                ExpenseDateUtc = e.ExpenseDateUtc,
                CategoryName = e.CategoryNameSnapshot,
                Amount = e.Amount,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RankedPartyRow>> GetTopCustomersAsync(
        ReportDateRange range, int? performedByUserId, int take = 5, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        return await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= range.StartUtc && s.SaleDateUtc < range.EndUtc && s.CustomerId != null)
            .GroupBy(s => new { s.CustomerId, s.Customer!.Name, s.Customer!.CustomerCode })
            .Select(g => new RankedPartyRow
            {
                PartyId = g.Key.CustomerId!.Value,
                Name = g.Key.Name,
                Code = g.Key.CustomerCode,
                Amount = g.Sum(x => x.GrandTotal),
                TransactionCount = g.Count(),
            })
            .OrderByDescending(r => r.Amount)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RankedPartyRow>> GetTopSuppliersAsync(
        ReportDateRange range, int? performedByUserId, int take = 5, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        return await db.Purchases.AsNoTracking()
            .Where(p => p.PurchaseDateUtc >= range.StartUtc && p.PurchaseDateUtc < range.EndUtc)
            .GroupBy(p => new { p.SupplierId, p.Supplier.Name, p.Supplier.SupplierCode })
            .Select(g => new RankedPartyRow
            {
                PartyId = g.Key.SupplierId,
                Name = g.Key.Name,
                Code = g.Key.SupplierCode,
                Amount = g.Sum(x => x.GrandTotal),
                TransactionCount = g.Count(),
            })
            .OrderByDescending(r => r.Amount)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    // ------------------------------------------------------------ trend charts (local-day bucketing)

    private async Task<ChartSeries> BuildDailySalesTrendAsync(DateTime nowLocal, CancellationToken cancellationToken)
    {
        const int days = 14;
        var startLocal = nowLocal.Date.AddDays(-(days - 1));
        var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime();

        var raw = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= startUtc)
            .Select(s => new { s.SaleDateUtc, s.GrandTotal })
            .ToListAsync(cancellationToken);

        var byDay = raw.GroupBy(s => s.SaleDateUtc.ToLocalTime().Date)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.GrandTotal));

        var points = Enumerable.Range(0, days)
            .Select(offset => startLocal.AddDays(offset))
            .Select(day => new ChartPoint { Label = day.ToString("dd-MMM"), Value = byDay.GetValueOrDefault(day) })
            .ToList();

        return new ChartSeries { Name = "Daily Sales", Points = points };
    }

    private async Task<ChartSeries> BuildWeeklySalesAsync(DateTime nowLocal, CancellationToken cancellationToken)
    {
        const int weeks = 10;
        var daysSinceMonday = ((int)nowLocal.DayOfWeek + 6) % 7;
        var thisWeekStart = nowLocal.Date.AddDays(-daysSinceMonday);
        var firstWeekStart = thisWeekStart.AddDays(-7 * (weeks - 1));
        var startUtc = DateTime.SpecifyKind(firstWeekStart, DateTimeKind.Local).ToUniversalTime();

        var raw = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= startUtc)
            .Select(s => new { s.SaleDateUtc, s.GrandTotal })
            .ToListAsync(cancellationToken);

        ChartPoint BucketFor(int weekIndex)
        {
            var weekStart = firstWeekStart.AddDays(7 * weekIndex);
            var weekEndExclusive = weekStart.AddDays(7);
            var total = raw
                .Where(s =>
                {
                    var local = s.SaleDateUtc.ToLocalTime();
                    return local >= weekStart && local < weekEndExclusive;
                })
                .Sum(s => s.GrandTotal);

            return new ChartPoint { Label = $"{weekStart:dd-MMM}", Value = total };
        }

        var points = Enumerable.Range(0, weeks).Select(BucketFor).ToList();
        return new ChartSeries { Name = "Weekly Sales", Points = points };
    }

    private async Task<ChartSeries> BuildMonthlySalesAsync(DateTime nowLocal, CancellationToken cancellationToken)
    {
        const int months = 12;
        var thisMonthStart = new DateTime(nowLocal.Year, nowLocal.Month, 1);
        var firstMonthStart = thisMonthStart.AddMonths(-(months - 1));
        var startUtc = DateTime.SpecifyKind(firstMonthStart, DateTimeKind.Local).ToUniversalTime();

        var raw = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= startUtc)
            .Select(s => new { s.SaleDateUtc, s.GrandTotal })
            .ToListAsync(cancellationToken);

        var points = BucketByMonth(raw.Select(s => (s.SaleDateUtc, s.GrandTotal)), firstMonthStart, months);
        return new ChartSeries { Name = "Monthly Sales", Points = points };
    }

    private async Task<(ChartSeries Sales, ChartSeries Expenses)> BuildSalesVsExpensesAsync(DateTime nowLocal, CancellationToken cancellationToken)
    {
        const int months = 6;
        var thisMonthStart = new DateTime(nowLocal.Year, nowLocal.Month, 1);
        var firstMonthStart = thisMonthStart.AddMonths(-(months - 1));
        var startUtc = DateTime.SpecifyKind(firstMonthStart, DateTimeKind.Local).ToUniversalTime();

        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= startUtc)
            .Select(s => new { s.SaleDateUtc, s.GrandTotal })
            .ToListAsync(cancellationToken);

        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDateUtc >= startUtc)
            .Select(e => new { e.ExpenseDateUtc, e.Amount })
            .ToListAsync(cancellationToken);

        var salesPoints = BucketByMonth(sales.Select(s => (s.SaleDateUtc, s.GrandTotal)), firstMonthStart, months);
        var expensePoints = BucketByMonth(expenses.Select(e => (e.ExpenseDateUtc, e.Amount)), firstMonthStart, months);

        return (
            new ChartSeries { Name = "Sales", Points = salesPoints },
            new ChartSeries { Name = "Expenses", Points = expensePoints });
    }

    private async Task<ChartSeries> BuildGrossProfitTrendAsync(DateTime nowLocal, CancellationToken cancellationToken)
    {
        const int months = 6;
        var thisMonthStart = new DateTime(nowLocal.Year, nowLocal.Month, 1);
        var firstMonthStart = thisMonthStart.AddMonths(-(months - 1));
        var startUtc = DateTime.SpecifyKind(firstMonthStart, DateTimeKind.Local).ToUniversalTime();

        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= startUtc)
            .Select(s => new { s.SaleDateUtc, s.GrandTotal })
            .ToListAsync(cancellationToken);

        var returns = await db.SalesReturns.AsNoTracking()
            .Where(r => r.ReturnDateUtc >= startUtc)
            .Select(r => new { r.ReturnDateUtc, r.TotalReturnAmount })
            .ToListAsync(cancellationToken);

        var soldCost = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= startUtc)
            .Select(i => new { i.Sale.SaleDateUtc, Cost = i.Quantity * i.Product.PurchasePrice })
            .ToListAsync(cancellationToken);

        var returnedCost = await db.SalesReturnItems.AsNoTracking()
            .Where(i => i.SalesReturn.ReturnDateUtc >= startUtc)
            .Select(i => new { i.SalesReturn.ReturnDateUtc, Cost = i.Quantity * i.Product.PurchasePrice })
            .ToListAsync(cancellationToken);

        ChartPoint BucketFor(int monthIndex)
        {
            var monthStart = firstMonthStart.AddMonths(monthIndex);
            var monthEndExclusive = monthStart.AddMonths(1);

            bool InMonth(DateTime utc)
            {
                var local = utc.ToLocalTime();
                return local >= monthStart && local < monthEndExclusive;
            }

            var revenue = sales.Where(s => InMonth(s.SaleDateUtc)).Sum(s => s.GrandTotal)
                - returns.Where(r => InMonth(r.ReturnDateUtc)).Sum(r => r.TotalReturnAmount);
            var cost = soldCost.Where(s => InMonth(s.SaleDateUtc)).Sum(s => s.Cost)
                - returnedCost.Where(r => InMonth(r.ReturnDateUtc)).Sum(r => r.Cost);

            return new ChartPoint { Label = monthStart.ToString("MMM yy"), Value = revenue - cost };
        }

        var points = Enumerable.Range(0, months).Select(BucketFor).ToList();
        return new ChartSeries { Name = "Gross Profit", Points = points };
    }

    private static List<ChartPoint> BucketByMonth(IEnumerable<(DateTime TimestampUtc, decimal Value)> rows, DateTime firstMonthStartLocal, int months)
    {
        var byMonth = rows
            .GroupBy(r => new DateTime(r.TimestampUtc.ToLocalTime().Year, r.TimestampUtc.ToLocalTime().Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Value));

        return Enumerable.Range(0, months)
            .Select(firstMonthStartLocal.AddMonths)
            .Select(month => new ChartPoint { Label = month.ToString("MMM yy"), Value = byMonth.GetValueOrDefault(month) })
            .ToList();
    }

    // ------------------------------------------------------------ range-scoped charts

    private async Task<ChartSeries> BuildProductCategorySalesAsync(ReportDateRange range, CancellationToken cancellationToken)
    {
        var raw = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc)
            .Select(i => new { CategoryName = i.Product.Category != null ? i.Product.Category.Name : "Uncategorized", i.LineTotal })
            .ToListAsync(cancellationToken);

        var points = raw.GroupBy(x => x.CategoryName)
            .Select(g => new ChartPoint { Label = g.Key, Value = g.Sum(x => x.LineTotal) })
            .OrderByDescending(p => p.Value)
            .Take(8)
            .ToList();

        return new ChartSeries { Name = "Category Sales", Points = points };
    }

    private async Task<ChartSeries> BuildPaymentMethodDistributionAsync(ReportDateRange range, CancellationToken cancellationToken)
    {
        var raw = await db.Payments.AsNoTracking()
            .Where(p => p.Sale.Status == SaleStatus.Completed && p.Sale.SaleDateUtc >= range.StartUtc && p.Sale.SaleDateUtc < range.EndUtc)
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var points = raw
            .Select(x => new ChartPoint { Label = ReportFormatting.FormatPaymentMethod(x.Method), Value = x.Total })
            .OrderByDescending(p => p.Value)
            .ToList();

        return new ChartSeries { Name = "Payment Methods", Points = points };
    }
}
