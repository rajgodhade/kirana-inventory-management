using Kirana.Application.Expenses;

namespace Kirana.Application.Reports;

/// <summary>
/// Expense breakdowns beyond what <see cref="IExpenseService.GetTotalsAsync"/> already provides
/// (PRD §51 "Expense Reports"): day-by-day and month-by-month grouping, and a longer trend line.
/// Gated by <see cref="Domain.Entities.PermissionKeys.ExpensesManage"/> — the same permission that
/// already protects the expense list itself, since a report is just another view of the same data.
/// </summary>
public interface IExpenseReportService
{
    Task<IReadOnlyList<ExpenseDailyRow>> GetDailyAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExpenseMonthlyRow>> GetMonthlyAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Monthly totals for the trailing <paramref name="months"/> months, ending this
    /// month — for the Expense Trend chart/table, independent of the selected report date range.</summary>
    Task<IReadOnlyList<ExpenseMonthlyRow>> GetTrendAsync(
        int months, int? performedByUserId, CancellationToken cancellationToken = default);
}
