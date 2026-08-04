namespace Kirana.Application.Reports;

/// <summary>
/// Estimated profit for a date range. Gated by
/// <see cref="Domain.Entities.PermissionKeys.ReportsViewProfit"/> specifically — a stricter gate
/// than the general <see cref="Domain.Entities.PermissionKeys.ReportsView"/> that covers the rest
/// of the reports, because margin is the single most sensitive figure in the store (PRD §6, §9).
/// </summary>
public interface IProfitReportService
{
    Task<ProfitSummary> GetSummaryAsync(ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);
}
