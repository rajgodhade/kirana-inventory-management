using Kirana.App.Printing;
using Kirana.App.Services;
using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Printing;
using Kirana.Application.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Kirana.App.Views;

public sealed partial class InvoicesPage : Page
{
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly IInvoicePrintService _invoicePrintService;
    private readonly IReportExportService _reportExportService;
    private readonly ManagementSession _session;
    private readonly InvoiceRefreshNotifier _refreshNotifier;

    public InvoicesViewModel ViewModel { get; }

    public InvoicesPage()
    {
        var services = App.Services;
        _session = services.GetRequiredService<ManagementSession>();
        _invoicePrintService = services.GetRequiredService<IInvoicePrintService>();
        _reportExportService = services.GetRequiredService<IReportExportService>();
        _refreshNotifier = services.GetRequiredService<InvoiceRefreshNotifier>();
        ViewModel = new InvoicesViewModel(
            services.GetRequiredService<IInvoiceService>(),
            services.GetRequiredService<ICustomerService>(),
            _session);

        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _refreshNotifier.InvoicesChanged += OnInvoicesChanged;
        await ViewModel.InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _refreshNotifier.InvoicesChanged -= OnInvoicesChanged;

    private void OnInvoicesChanged(object? sender, EventArgs e) =>
        _ = DispatcherQueue.TryEnqueue(async () => await ViewModel.SearchAsync());

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        }
    }

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e) => await ViewModel.SearchAsync();
    private async void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => await ViewModel.SearchAsync();
    private async void OnTodayClick(object sender, RoutedEventArgs e) => await ViewModel.ShowTodayAsync();
    private async void OnClearDatesClick(object sender, RoutedEventArgs e) => await ViewModel.ClearDateFiltersAsync();
    private async void OnClearFiltersClick(object sender, RoutedEventArgs e) => await ViewModel.ClearFiltersAsync();

    private void OnRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is InvoiceRowViewModel row && !IsInsideButton(e.OriginalSource as DependencyObject))
        {
            Frame.Navigate(typeof(InvoiceDetailsPage), row.SaleId);
        }
    }

    private void OnViewClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is InvoiceRowViewModel row)
        {
            Frame.Navigate(typeof(InvoiceDetailsPage), row.SaleId);
        }
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is InvoiceRowViewModel row)
        {
            await ShowPrintPreviewAsync(row.SaleId, InvoiceFormat.Thermal80mm, isReprint: false);
        }
    }

    private async void OnPrintGstClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is InvoiceRowViewModel row)
        {
            await ShowPrintPreviewAsync(row.SaleId, InvoiceFormat.A4, isReprint: true);
        }
    }

    private async void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is InvoiceRowViewModel row)
        {
            await ShowPrintPreviewAsync(row.SaleId, InvoiceFormat.Thermal80mm, isReprint: true);
        }
    }

    private void OnReturnClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SalesReturnsPage));

    private async void OnExportClick(object sender, RoutedEventArgs e) => await ExportAsync(ReportExportFormat.Csv);
    private async void OnExportPdfClick(object sender, RoutedEventArgs e) =>
        await ReportExportHelper.ExportToPdfAsync(App.MainWindow, ViewModel.BuildExportData(), _reportExportService, ViewModel.CurrentUserId);
    private async void OnExportExcelClick(object sender, RoutedEventArgs e) => await ExportAsync(ReportExportFormat.Excel);

    private async Task ExportAsync(ReportExportFormat format) =>
        await ReportExportHelper.ExportToFileAsync(App.MainWindow, ViewModel.BuildExportData(), format, _reportExportService, ViewModel.CurrentUserId);

    private async Task ShowPrintPreviewAsync(int saleId, InvoiceFormat format, bool isReprint)
    {
        try
        {
            var document = await _invoicePrintService.GetInvoiceDocumentAsync(saleId);
            var viewModel = new InvoicePreviewViewModel(document, format, _session.CurrentUser?.Id, isReprint, _invoicePrintService);
            await new InvoicePreviewDialog(viewModel).Themed(XamlRoot).ShowAsync();
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Button) return true;
        }

        return false;
    }
}
