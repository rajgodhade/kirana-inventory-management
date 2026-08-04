namespace Kirana.Application.Reports;

/// <summary>
/// The Management Dashboard (PRD §51): KPI tiles, charts and recent activity. Every member
/// requires <see cref="Domain.Entities.PermissionKeys.ReportsView"/>; profit-bearing fields are
/// additionally withheld unless the caller also holds
/// <see cref="Domain.Entities.PermissionKeys.ReportsViewProfit"/> — see <see cref="DashboardSummary"/>.
/// </summary>
public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<DashboardCharts> GetChartsAsync(ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentSaleRow>> GetRecentSalesAsync(int? performedByUserId, int take = 6, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentPurchaseRow>> GetRecentPurchasesAsync(int? performedByUserId, int take = 6, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentReturnRow>> GetRecentReturnsAsync(int? performedByUserId, int take = 6, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentExpenseRow>> GetRecentExpensesAsync(int? performedByUserId, int take = 6, CancellationToken cancellationToken = default);

    /// <summary>Customers ranked by revenue within the range, largest first.</summary>
    Task<IReadOnlyList<RankedPartyRow>> GetTopCustomersAsync(ReportDateRange range, int? performedByUserId, int take = 5, CancellationToken cancellationToken = default);

    /// <summary>Suppliers ranked by purchase value within the range, largest first.</summary>
    Task<IReadOnlyList<RankedPartyRow>> GetTopSuppliersAsync(ReportDateRange range, int? performedByUserId, int take = 5, CancellationToken cancellationToken = default);
}
