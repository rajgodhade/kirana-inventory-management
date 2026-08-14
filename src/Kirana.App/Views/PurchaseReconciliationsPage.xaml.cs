using Kirana.App.Printing;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Application.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class PurchaseReconciliationsPage : Page
{
    private readonly IReportExportService _exportService;
    public PurchaseReconciliationsViewModel ViewModel { get; }

    public PurchaseReconciliationsPage()
    {
        var services = App.Services;
        ViewModel = new(
            services.GetRequiredService<IPurchaseReconciliationService>(),
            services.GetRequiredService<ISupplierService>(),
            services.GetRequiredService<ManagementSession>());
        _exportService = services.GetRequiredService<IReportExportService>();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private void OnViewClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PurchaseReconciliationRowViewModel row)
            Frame.Navigate(typeof(PurchaseReconciliationDetailsPage), row.PurchaseOrderId);
    }

    private async void OnKpiTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string filter)
        {
            ViewModel.SelectedFilter = filter;
            await ViewModel.SearchAsync();
        }
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) await ViewModel.SearchAsync();
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        ViewModel.SearchText = sender.Text;
        ViewModel.UpdateSearchSuggestions(sender.Text);
        sender.IsSuggestionListOpen = e.Reason == AutoSuggestionBoxTextChangeReason.UserInput && ViewModel.SearchSuggestions.Count > 0;
    }

    private void OnSearchGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is AutoSuggestBox box)
        {
            ViewModel.UpdateSearchSuggestions(box.Text);
            box.IsSuggestionListOpen = ViewModel.SearchSuggestions.Count > 0;
        }
    }

    private async void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs e)
    {
        if (e.SelectedItem is not SearchSuggestionItem item) return;
        ViewModel.SearchText = item.Value;
        sender.Text = item.Value;
        await ViewModel.SearchAsync();
    }

    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        ViewModel.SearchText = e.ChosenSuggestion is SearchSuggestionItem item ? item.Value : e.QueryText;
        await ViewModel.SearchAsync();
    }

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) await ViewModel.SearchAsync();
    }

    private async void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (IsLoaded) await ViewModel.SearchAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();
    private async void OnClearClick(object sender, RoutedEventArgs e) { ViewModel.ClearFilters(); await ViewModel.SearchAsync(); }
    private async void OnExportCsvClick(object sender, RoutedEventArgs e) => await ExportAsync(ReportExportFormat.Csv);
    private async void OnExportExcelClick(object sender, RoutedEventArgs e) => await ExportAsync(ReportExportFormat.Excel);
    private async void OnExportPdfClick(object sender, RoutedEventArgs e) =>
        await ReportExportHelper.ExportToPdfAsync(App.MainWindow, ViewModel.BuildExportData(), _exportService, ViewModel.CurrentUserId);
    private async Task ExportAsync(ReportExportFormat format) =>
        await ReportExportHelper.ExportToFileAsync(App.MainWindow, ViewModel.BuildExportData(), format, _exportService, ViewModel.CurrentUserId);
}
