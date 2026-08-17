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

        // Cost of goods sold, from the cost SNAPSHOTTED on each line at the moment it was sold
        // (Phase 17A) — never the product's current purchase price. Recomputing from master data
        // meant raising a cost today silently rewrote last month's reported profit.
        var soldLines = db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc);

        // Lines with no snapshot are EXCLUDED from cost rather than counted at zero. A null is
        // "we do not know what this cost" — every sale predating 17A — and folding it in as zero
        // would report those lines at 100% margin. They are counted separately and surfaced, so a
        // period containing them is visibly partial instead of quietly overstated.
        var soldCost = await soldLines
            .Where(i => i.UnitCostSnapshot != null)
            .SumAsync(i => (decimal?)(i.Quantity * i.UnitCostSnapshot!.Value), cancellationToken) ?? 0m;

        var knownCostLines = await soldLines.CountAsync(i => i.UnitCostSnapshot != null, cancellationToken);
        var unknownCostLines = await soldLines.CountAsync(i => i.UnitCostSnapshot == null, cancellationToken);

        // Returned goods still net off at the ORIGINATING line's snapshot, reached through
        // SaleItem — SalesReturnItem carries no cost of its own. That is deliberate for 17A: it
        // keeps returns on the same historical basis as the sale without adding a second, possibly
        // divergent, cost record. Phase 17B decides whether the return needs its own snapshot.
        var returnedCost = await db.SalesReturnItems.AsNoTracking()
            .Where(i => i.SalesReturn.ReturnDateUtc >= range.StartUtc && i.SalesReturn.ReturnDateUtc < range.EndUtc)
            .Where(i => i.SaleItem.UnitCostSnapshot != null)
            .SumAsync(i => (decimal?)(i.Quantity * i.SaleItem.UnitCostSnapshot!.Value), cancellationToken) ?? 0m;

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
            KnownCostLineCount = knownCostLines,
            UnknownCostLineCount = unknownCostLines,
            GrossProfit = grossProfit,
            Expenses = expenses,
            NetProfit = netProfit,
        };
    }
}
