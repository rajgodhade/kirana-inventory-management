using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class GoodsReceiptsPage : Page
{
    public GoodsReceiptsViewModel ViewModel { get; }
    public GoodsReceiptsPage()
    {
        ViewModel = new(App.Services.GetRequiredService<IGoodsReceiptService>(), App.Services.GetRequiredService<ISupplierService>(), App.Services.GetRequiredService<ManagementSession>());
        InitializeComponent(); Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }
    private async void OnViewClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not GoodsReceiptRowViewModel row || await ViewModel.GetAsync(row.Id) is not { } receipt) return;
        var dialog = new GoodsReceiptDetailsDialog(receipt).Themed(XamlRoot);
        await dialog.ShowAsync();
        if (dialog.ReconciliationRequested) Frame.Navigate(typeof(PurchaseReconciliationDetailsPage), row.PurchaseOrderId);
        else if (dialog.PurchaseOrderRequested) await new PurchaseOrderDetailsDialog(receipt.PurchaseOrder).Themed(XamlRoot).ShowAsync();
        else if (dialog.PurchaseRequested && receipt.Purchase is not null) await new PurchaseDetailsDialog(receipt.Purchase).Themed(XamlRoot).ShowAsync();
    }
    private void OnCreatePurchaseClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is GoodsReceiptRowViewModel row) Frame.Navigate(typeof(PurchaseEntryPage), row.Id); }
    private void OnReconcileClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is GoodsReceiptRowViewModel row) Frame.Navigate(typeof(PurchaseReconciliationDetailsPage), row.PurchaseOrderId); }
    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not GoodsReceiptRowViewModel row) return;
        var reason = new TextBox { Header = "Cancellation reason", MinWidth = 360 };
        var dialog = new ContentDialog { Title = $"Cancel {row.Number}?", Content = reason, PrimaryButtonText = "Cancel Receipt", CloseButtonText = "Keep Receipt", DefaultButton = ContentDialogButton.Close }.Themed(XamlRoot);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { await ViewModel.CancelAsync(row.Id, reason.Text); await ViewModel.SearchAsync(); } catch (Exception ex) { await ShowErrorAsync(ex.Message); }
    }
    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e) => await ViewModel.SearchAsync();
    private async void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) => await ViewModel.SearchAsync();
    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) await ViewModel.SearchAsync(); }
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
    private async void OnClearClick(object sender, RoutedEventArgs e) { ViewModel.ClearFilters(); await ViewModel.SearchAsync(); }
    private async Task ShowErrorAsync(string message) => await new ContentDialog { Title = "Goods receipt", Content = message, CloseButtonText = "Close" }.Themed(XamlRoot).ShowAsync();
}
