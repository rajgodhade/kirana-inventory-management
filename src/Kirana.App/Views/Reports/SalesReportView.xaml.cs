using Kirana.App.Printing;
using Kirana.App.ViewModels.Reports;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views.Reports;

public sealed partial class SalesReportView : UserControl
{
    private readonly IReportExportService _exportService;
    private readonly ManagementSession _session;

    public SalesReportTabViewModel ViewModel { get; }

    public SalesReportView()
    {
        var services = App.Services;
        _exportService = services.GetRequiredService<IReportExportService>();
        _session = services.GetRequiredService<ManagementSession>();
        ViewModel = new SalesReportTabViewModel(
            services.GetRequiredService<ISalesReportService>(),
            _session);

        InitializeComponent();
    }

    public Task EnsureLoadedAsync() => ViewModel.LoadAsync();

    private async void OnRefreshClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnFilterChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnExportSalesCsvClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportAsync(ViewModel.BuildSalesExportData, ReportExportFormat.Csv);
    private async void OnExportSalesExcelClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportAsync(ViewModel.BuildSalesExportData, ReportExportFormat.Excel);
    private async void OnExportSalesPdfClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportPdfAsync(ViewModel.BuildSalesExportData);

    private async void OnExportGstCsvClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportAsync(ViewModel.BuildGstExportData, ReportExportFormat.Csv);
    private async void OnExportGstExcelClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportAsync(ViewModel.BuildGstExportData, ReportExportFormat.Excel);
    private async void OnExportGstPdfClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportPdfAsync(ViewModel.BuildGstExportData);

    private async Task ExportAsync(Func<ReportExportData> build, ReportExportFormat format)
    {
        try
        {
            await ReportExportHelper.ExportToFileAsync(App.MainWindow, build(), format, _exportService, _session.CurrentUser?.Id);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task ExportPdfAsync(Func<ReportExportData> build)
    {
        try
        {
            await ReportExportHelper.ExportToPdfAsync(App.MainWindow, build(), _exportService, _session.CurrentUser?.Id);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
