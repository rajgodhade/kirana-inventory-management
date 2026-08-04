using Kirana.App.Printing;
using Kirana.App.ViewModels.Reports;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views.Reports;

public sealed partial class InventoryReportView : UserControl
{
    private readonly IReportExportService _exportService;
    private readonly ManagementSession _session;

    public InventoryReportTabViewModel ViewModel { get; }

    public InventoryReportView()
    {
        var services = App.Services;
        _exportService = services.GetRequiredService<IReportExportService>();
        _session = services.GetRequiredService<ManagementSession>();
        ViewModel = new InventoryReportTabViewModel(
            services.GetRequiredService<IInventoryReportService>(),
            _session);

        InitializeComponent();
    }

    public Task EnsureLoadedAsync() => ViewModel.LoadAsync();

    private async void OnRefreshClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnExportCsvClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportAsync(ReportExportFormat.Csv);
    private async void OnExportExcelClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportAsync(ReportExportFormat.Excel);

    private async void OnExportPdfClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            await ReportExportHelper.ExportToPdfAsync(App.MainWindow, ViewModel.BuildExportData(), _exportService, _session.CurrentUser?.Id);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task ExportAsync(ReportExportFormat format)
    {
        try
        {
            await ReportExportHelper.ExportToFileAsync(App.MainWindow, ViewModel.BuildExportData(), format, _exportService, _session.CurrentUser?.Id);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
