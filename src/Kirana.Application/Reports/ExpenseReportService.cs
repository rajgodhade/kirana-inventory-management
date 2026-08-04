using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Reports;

public sealed class ExpenseReportService(IKiranaDbContext db, IPermissionEnforcer permissionEnforcer) : IExpenseReportService
{
    public async Task<IReadOnlyList<ExpenseDailyRow>> GetDailyAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        var raw = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDateUtc >= range.StartUtc && e.ExpenseDateUtc < range.EndUtc)
            .Select(e => new { e.ExpenseDateUtc, e.Amount })
            .ToListAsync(cancellationToken);

        return raw.GroupBy(e => DateOnly.FromDateTime(e.ExpenseDateUtc.ToLocalTime()))
            .Select(g => new ExpenseDailyRow { Date = g.Key, Amount = g.Sum(x => x.Amount), Count = g.Count() })
            .OrderBy(r => r.Date)
            .ToList();
    }

    public async Task<IReadOnlyList<ExpenseMonthlyRow>> GetMonthlyAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        var raw = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDateUtc >= range.StartUtc && e.ExpenseDateUtc < range.EndUtc)
            .Select(e => new { e.ExpenseDateUtc, e.Amount })
            .ToListAsync(cancellationToken);

        return raw.GroupBy(e =>
            {
                var local = e.ExpenseDateUtc.ToLocalTime();
                return (local.Year, local.Month);
            })
            .Select(g => new ExpenseMonthlyRow
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Label = new DateOnly(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                Amount = g.Sum(x => x.Amount),
                Count = g.Count(),
            })
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .ToList();
    }

    public async Task<IReadOnlyList<ExpenseMonthlyRow>> GetTrendAsync(
        int months, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        var nowLocal = DateTime.Now;
        var thisMonthStart = new DateTime(nowLocal.Year, nowLocal.Month, 1);
        var firstMonthStart = thisMonthStart.AddMonths(-(months - 1));
        var startUtc = DateTime.SpecifyKind(firstMonthStart, DateTimeKind.Local).ToUniversalTime();

        var raw = await db.Expenses.AsNoTracking()
            .Where(e => e.ExpenseDateUtc >= startUtc)
            .Select(e => new { e.ExpenseDateUtc, e.Amount })
            .ToListAsync(cancellationToken);

        var byMonth = raw.GroupBy(e =>
            {
                var local = e.ExpenseDateUtc.ToLocalTime();
                return new DateTime(local.Year, local.Month, 1);
            })
            .ToDictionary(g => g.Key, g => (Amount: g.Sum(x => x.Amount), Count: g.Count()));

        return Enumerable.Range(0, months)
            .Select(firstMonthStart.AddMonths)
            .Select(month =>
            {
                byMonth.TryGetValue(month, out var agg);
                return new ExpenseMonthlyRow
                {
                    Year = month.Year,
                    Month = month.Month,
                    Label = month.ToString("MMM yyyy"),
                    Amount = agg.Amount,
                    Count = agg.Count,
                };
            })
            .ToList();
    }
}
