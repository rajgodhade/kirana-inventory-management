using Kirana.Application.Reports;

namespace Kirana.Application.Export;

/// <summary>Every dataset the Export Center can dump. Kept as an enum so the UI, the audit trail
/// and the permission map all name the same thing.</summary>
public enum ExportDataset
{
    Products,
    Categories,
    Brands,
    Customers,
    Suppliers,
    Inventory,
    Sales,
    Purchases,
    Expenses,
    Promotions,
}

/// <summary>
/// Flattens a whole data domain into the same <see cref="ReportExportData"/> shape the Phase 10
/// report screens already export through, so bulk data export reuses the existing CSV/XLSX writers
/// rather than growing a second one (PRD §40).
/// </summary>
public interface IDataExportService
{
    /// <summary>The permission a user needs to export this dataset — the same key that already
    /// gates seeing the data anywhere else in the app, so export can never become a side door
    /// around an existing restriction.</summary>
    string RequiredPermissionFor(ExportDataset dataset);

    string DisplayNameFor(ExportDataset dataset);

    /// <summary>
    /// Builds the full export for a dataset. Transactional datasets (Sales, Purchases, Expenses)
    /// accept an optional date window; passing none exports everything, which is the point of the
    /// Export Center as opposed to the date-filtered Reports Hub.
    /// </summary>
    Task<ReportExportData> BuildExportAsync(
        ExportDataset dataset,
        int? performedByUserId,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        CancellationToken cancellationToken = default);
}
