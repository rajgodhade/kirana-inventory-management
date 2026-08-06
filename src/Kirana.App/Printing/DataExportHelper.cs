using Kirana.Application.Abstractions;
using Kirana.Application.Reports;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Kirana.App.Printing;

/// <summary>
/// Writes an Export Center dataset to a file the user picks. Deliberately separate from
/// <see cref="ReportExportHelper"/> despite the near-identical shape: that one audits every export
/// as <c>ReportExported</c>, which would misattribute a full customer-list dump as a report pull.
/// The file-building itself reuses <see cref="IReportExportService"/> unchanged.
/// </summary>
public static class DataExportHelper
{
    /// <summary>Returns the saved file's path, or null if the user cancelled.</summary>
    public static async Task<string?> ExportToFileAsync(
        Window window,
        ReportExportData data,
        ReportExportFormat format,
        IReportExportService exportService,
        IAuditLogger auditLogger,
        int? performedByUserId)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = $"kirana-{data.Title.ToLowerInvariant()}-{DateTime.Now:yyyyMMdd}";

        if (format == ReportExportFormat.Csv)
        {
            picker.FileTypeChoices.Add("CSV (comma-separated)", [".csv"]);
        }
        else
        {
            picker.FileTypeChoices.Add("Excel Workbook", [".xlsx"]);
        }

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return null; // cancelled — nothing written, nothing to audit
        }

        if (format == ReportExportFormat.Csv)
        {
            await FileIO.WriteTextAsync(file, exportService.BuildCsv(data));
        }
        else
        {
            await FileIO.WriteBytesAsync(file, exportService.BuildExcel(data));
        }

        // Who pulled a full copy of the customer or supplier list out of the system is exactly the
        // kind of thing the audit trail exists for (PRD §9, §37).
        await auditLogger.RecordAsync(
            performedByUserId, "DataExported", data.Title, null,
            newValue: $"{format}, {data.Rows.Count} row(s)");

        return file.Path;
    }
}
