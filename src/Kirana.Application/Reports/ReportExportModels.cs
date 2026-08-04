namespace Kirana.Application.Reports;

/// <summary>
/// A generic tabular shape every exportable report is flattened into before being handed to
/// <see cref="IReportExportService"/>. Keeping export format-agnostic of the report's own DTOs
/// means CSV/Excel/print all render exactly the same numbers from exactly the same rows — there is
/// only one place ("build a ReportExportData") where a report decides what its export looks like.
/// </summary>
public sealed class ReportExportData
{
    public required string Title { get; init; }

    /// <summary>Shown under the title — typically the resolved date-range label and any active filters.</summary>
    public string? Subtitle { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }

    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }
}
