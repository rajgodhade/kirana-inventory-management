namespace Kirana.Application.Reports;

/// <summary>Sales and GST reports (PRD §51). Gated by <see cref="Domain.Entities.PermissionKeys.ReportsView"/>.</summary>
public interface ISalesReportService
{
    Task<SalesReportSummary> GetSummaryAsync(
        ReportDateRange range, ReportFilter? filter, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<GstReport> GetGstReportAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default,
        ReportFilter? filter = null);
}
