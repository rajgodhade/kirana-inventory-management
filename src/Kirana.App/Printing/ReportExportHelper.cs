using Kirana.Application.Reports;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Kirana.App.Printing;

/// <summary>
/// Drives the three "Export" choices a report screen offers (PRD §51 "Export"): CSV and Excel
/// write a file the user picks a location for; PDF opens the native print dialog against a
/// <see cref="ReportPrintHelper"/> page, exactly like every other document this app prints. Every
/// successful export is logged via <see cref="IReportExportService.LogExportAsync"/> regardless of
/// which format was chosen.
/// </summary>
public static class ReportExportHelper
{
    public static async Task ExportToFileAsync(
        Window window, ReportExportData data, ReportExportFormat format, IReportExportService exportService, int? performedByUserId)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = SanitizeFileName(data.Title);

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
            return; // user cancelled — nothing was exported, nothing to audit
        }

        if (format == ReportExportFormat.Csv)
        {
            await FileIO.WriteTextAsync(file, exportService.BuildCsv(data));
        }
        else
        {
            await FileIO.WriteBytesAsync(file, exportService.BuildExcel(data));
        }

        await exportService.LogExportAsync(data.Title, format, performedByUserId);
    }

    public static async Task ExportToPdfAsync(
        Window window, ReportExportData data, IReportExportService exportService, int? performedByUserId)
    {
        using var helper = new ReportPrintHelper(window, data);
        await helper.ShowPrintUIAsync();

        // The user may have cancelled the system dialog rather than actually printed/saved a PDF —
        // there is no reliable signal either way from PrintManager, so this logs "asked to export,"
        // the same accepted imprecision Phase 5's invoice reprint audit already lives with.
        await exportService.LogExportAsync(data.Title, ReportExportFormat.Pdf, performedByUserId);
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return cleaned.Length == 0 ? "Report" : cleaned;
    }
}
