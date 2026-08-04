using Kirana.App.Printing;
using Kirana.App.ViewModels.Reports;
using Kirana.Application.Authentication;
using Kirana.Application.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views.Reports;

public sealed partial class ProfitReportView : UserControl
{
    private readonly IReportExportService _exportService;
    private readonly ManagementSession _session;

    public ProfitReportTabViewModel ViewModel { get; }

    public ProfitReportView()
    {
        var services = App.Services;
        _exportService = services.GetRequiredService<IReportExportService>();
        _session = services.GetRequiredService<ManagementSession>();
        ViewModel = new ProfitReportTabViewModel(
            services.GetRequiredService<IProfitReportService>(),
            _session);

        InitializeComponent();
    }

    public Task EnsureLoadedAsync() => ViewModel.LoadAsync();

    private async void OnRefreshClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnDateFilterChanged(object sender, SelectionChangedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnDateFilterChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs e) => await ViewModel.LoadAsync();

    private async void OnExportCsvClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportAsync(ReportExportFormat.Csv);
    private async void OnExportExcelClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => await ExportAsync(ReportExportFormat.Excel);

    private async void OnExportPdfClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        => await ReportExportHelper.ExportToPdfAsync(App.MainWindow, ViewModel.BuildExportData(), _exportService, _session.CurrentUser?.Id);

    private async Task ExportAsync(ReportExportFormat format)
        => await ReportExportHelper.ExportToFileAsync(App.MainWindow, ViewModel.BuildExportData(), format, _exportService, _session.CurrentUser?.Id);
}
