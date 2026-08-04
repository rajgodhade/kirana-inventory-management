using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Reports;

public sealed class ProfitReportService(IKiranaDbContext db, IPermissionEnforcer permissionEnforcer) : IProfitReportService
{
    public async Task<ProfitSummary> GetSummaryAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsViewProfit, cancellationToken);

        var grossSales = await db.Sales.AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed && s.SaleDateUtc >= range.StartUtc && s.SaleDateUtc < range.EndUtc)
            .SumAsync(s => (decimal?)s.GrandTotal, cancellationToken) ?? 0m;

        var returns = await db.SalesReturns.AsNoTracking()
            .Where(r => r.ReturnDateUtc >= range.StartUtc && r.ReturnDateUtc < range.EndUtc)
            .SumAsync(r => (decimal?)r.TotalReturnAmount, cancellationToken) ?? 0m;

        var revenue = grossSales - returns;

        // Cost of goods sold: quantity × each product's CURRENT purchase price (see the class doc
        // on ProfitSummary for why this is an estimate rather than a true historical cost basis),
        // netted against the same calculation for goods returned in the period.
        var soldCost = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc)
            .SumAsync(i => (decimal?)(i.Quantity * i.Product.PurchasePrice), cancellationToken) ?? 0m;

        var returnedCost = await db.SalesReturnItems.AsNoTracking()
            .Where(i => i.SalesReturn.ReturnDateUtc >= range.StartUtc && i.SalesReturn.ReturnDateUtc < range.EndUtc)
            .SumAsync(i => (decimal?)(i.Quantity * i.Product.PurchasePrice), cancellationToken) ?? 0m;

        var costOfGoodsSold = soldCost - returnedCost;
        var grossProfit = revenue - costOfGoodsSold;

        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDateUtc >= range.StartUtc && e.ExpenseDateUtc < range.EndUtc)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;

        var netProfit = grossProfit - expenses;

        return new ProfitSummary
        {
            Range = range,
            Revenue = revenue,
            GrossSales = grossSales,
            Returns = returns,
            CostOfGoodsSold = costOfGoodsSold,
            GrossProfit = grossProfit,
            Expenses = expenses,
            NetProfit = netProfit,
        };
    }
}
