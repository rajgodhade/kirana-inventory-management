namespace Kirana.Application.Reports;

public enum ReportExportFormat
{
    Csv,
    Excel,

    /// <summary>Not built by <see cref="IReportExportService"/> — PDF is produced by printing to
    /// "Microsoft Print to PDF" through the native print pipeline in the App layer. This value
    /// exists only so <see cref="IReportExportService.LogExportAsync"/> can record which export
    /// path a user took.</summary>
    Pdf,
}

/// <summary>
/// Turns a <see cref="ReportExportData"/> into bytes ready to write to disk. Deliberately pure
/// (no file I/O, no permission check) — by the time a caller has a <see cref="ReportExportData"/>
/// to export, it already came from a permission-gated report query, and the App layer is the one
/// that knows how to ask Windows for a save location. PDF export is not here: it reuses the
/// existing native print pipeline (PRD "reuse the existing printing/document infrastructure"),
/// which is a WinUI concern that belongs in the App project, not Application.
/// </summary>
public interface IReportExportService
{
    string BuildCsv(ReportExportData data);

    /// <summary>
    /// Produces a minimal single-sheet .xlsx (Office Open XML) using only <see cref="System.IO.Compression"/>
    /// from the base class library — no third-party spreadsheet package. A full-featured library
    /// would add formatting Kirana's reports don't need; this writes exactly the rows and a bold
    /// header, which is what "export to Excel" means for a report table.
    /// </summary>
    byte[] BuildExcel(ReportExportData data);

    /// <summary>Records that a report was exported — the export itself is not a mutation, but who
    /// pulled what financial data out of the system is worth an audit trail (PRD §9, §37).</summary>
    Task LogExportAsync(string reportName, ReportExportFormat format, int? performedByUserId, CancellationToken cancellationToken = default);
}
