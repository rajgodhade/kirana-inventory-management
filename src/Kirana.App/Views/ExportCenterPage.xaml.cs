using Kirana.App.Printing;
using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Export;
using Kirana.Application.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class ExportCenterPage : Page
{
    private readonly IDataExportService _dataExportService;
    private readonly IReportExportService _reportExportService;
    private readonly IAuditLogger _auditLogger;
    private readonly ManagementSession _session;

    public ExportCenterViewModel ViewModel { get; }

    public ExportCenterPage()
    {
        var services = App.Services;
        _dataExportService = services.GetRequiredService<IDataExportService>();
        _reportExportService = services.GetRequiredService<IReportExportService>();
        _auditLogger = services.GetRequiredService<IAuditLogger>();
        _session = services.GetRequiredService<ManagementSession>();

        ViewModel = new ExportCenterViewModel(_dataExportService, _session);
        InitializeComponent();
    }

    private async void OnExportCsvClick(object sender, RoutedEventArgs e) =>
        await ExportAsync(sender, ReportExportFormat.Csv);

    private async void OnExportExcelClick(object sender, RoutedEventArgs e) =>
        await ExportAsync(sender, ReportExportFormat.Excel);

    private async Task ExportAsync(object sender, ReportExportFormat format)
    {
        if ((sender as FrameworkElement)?.Tag is not ExportDatasetViewModel item)
        {
            return;
        }

        ViewModel.ErrorMessage = null;
        ViewModel.StatusMessage = null;
        ViewModel.IsBusy = true;

        try
        {
            var data = await _dataExportService.BuildExportAsync(item.Dataset, _session.CurrentUser?.Id);

            var savedPath = await DataExportHelper.ExportToFileAsync(
                App.MainWindow, data, format, _reportExportService, _auditLogger, _session.CurrentUser?.Id);

            if (savedPath is not null)
            {
                ViewModel.StatusMessage = $"{item.Title} exported ({data.Rows.Count} row(s)) to {savedPath}.";
            }
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Could not export {item.Title}: {ex.Message}";
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private void OnGoToReportsClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ReportsHubPage));
}
