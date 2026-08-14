using System.Text;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;

namespace Kirana.App.Views;

public sealed partial class ReplenishmentPage : Page
{
    public ReplenishmentViewModel ViewModel { get; }

    public ReplenishmentPage()
    {
        ViewModel = new(
            App.Services.GetRequiredService<IReplenishmentService>(),
            App.Services.GetRequiredService<ISupplierService>(),
            App.Services.GetRequiredService<ManagementSession>());
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.RefreshAsync();
    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) await ViewModel.RefreshAsync();
    }
    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await ViewModel.RefreshAsync(); }
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
        await ViewModel.RefreshAsync();
    }
    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        ViewModel.SearchText = e.ChosenSuggestion is SearchSuggestionItem item ? item.Value : e.QueryText;
        await ViewModel.RefreshAsync();
    }
    private void OnRowSelectionChanged(object sender, RoutedEventArgs e) => ViewModel.SelectionChanged();
    private async void OnClearClick(object sender, RoutedEventArgs e) { ViewModel.ClearFilters(); await ViewModel.RefreshAsync(); }
    private async void OnCreateSelectedClick(object sender, RoutedEventArgs e) =>
        await NavigateToPurchaseOrderAsync(ViewModel.Rows.Where(r => r.IsSelected).Select(r => r.ProductId).ToArray());
    private async void OnCreateRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ReplenishmentRowViewModel row)
            await NavigateToPurchaseOrderAsync([row.ProductId]);
    }
    private async Task NavigateToPurchaseOrderAsync(IReadOnlyCollection<int> productIds)
    {
        var prefill = await ViewModel.BuildPrefillAsync(productIds, ViewModel.Rows.Select(row => row.ProductId).ToArray());
        if (prefill is not null) Frame.Navigate(typeof(PurchaseOrderEntryPage), prefill);
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = $"replenishment-{DateTime.Now:yyyyMMdd-HHmm}" };
        picker.FileTypeChoices.Add("CSV", [".csv"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
        var csv = new StringBuilder("Product,Code,Current,Reorder Level,Target,Open PO,Suggested,Unit,Supplier,Estimated Unit Cost,Estimated Value,Status\r\n");
        foreach (var row in ViewModel.Rows)
        {
            var x = row.Recommendation;
            csv.AppendLine(string.Join(',', Csv(x.ProductName), Csv(x.ProductCode), x.CurrentStock, x.ReorderLevel,
                x.TargetStock, x.OpenPurchaseOrderQuantity, x.SuggestedQuantity, Csv(row.Unit), Csv(row.Supplier),
                x.EstimatedUnitCost?.ToString("0.00") ?? "", x.EstimatedOrderValue?.ToString("0.00") ?? "", Csv(row.Status)));
        }
        await Windows.Storage.FileIO.WriteTextAsync(file, csv.ToString());
    }
}
