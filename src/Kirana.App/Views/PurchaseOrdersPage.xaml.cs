using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class PurchaseOrdersPage : Page
{
    public PurchaseOrdersViewModel ViewModel { get; }
    public PurchaseOrdersPage()
    {
        ViewModel = new(App.Services.GetRequiredService<IPurchaseOrderService>(), App.Services.GetRequiredService<ManagementSession>());
        InitializeComponent(); Loaded += async (_, _) => await ViewModel.SearchAsync();
    }
    private void OnNewClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PurchaseOrderEntryPage));
    private void OnEditClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is PurchaseOrderRowViewModel row) Frame.Navigate(typeof(PurchaseOrderEntryPage), row.Id); }
    private void OnReceiveClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is PurchaseOrderRowViewModel row) Frame.Navigate(typeof(GoodsReceiptEntryPage), row.Id); }
    private void OnReconcileClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is PurchaseOrderRowViewModel row) Frame.Navigate(typeof(PurchaseReconciliationDetailsPage), row.Id); }
    private async void OnViewClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is PurchaseOrderRowViewModel row && await ViewModel.GetAsync(row.Id) is { } order) { var dialog = new PurchaseOrderDetailsDialog(order).Themed(XamlRoot); var result = await dialog.ShowAsync(); if (dialog.ReconciliationRequested) Frame.Navigate(typeof(PurchaseReconciliationDetailsPage), row.Id); else if (result == ContentDialogResult.Secondary) Frame.Navigate(typeof(GoodsReceiptEntryPage), row.Id); } }
    private async void OnSubmitClick(object sender, RoutedEventArgs e) { if ((sender as Button)?.Tag is PurchaseOrderRowViewModel row) { try { await ViewModel.SubmitAsync(row.Id); await ViewModel.SearchAsync(); } catch (Exception ex) { await ShowMessageAsync("Could not submit", ex.Message); } } }
    private async void OnCancelOrderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PurchaseOrderRowViewModel row) return;
        var reason = new TextBox { Header = "Cancellation reason", AcceptsReturn = true, MinWidth = 360 };
        var dialog = new ContentDialog { Title = $"Cancel {row.Number}?", Content = reason, PrimaryButtonText = "Cancel Order", CloseButtonText = "Keep Order", DefaultButton = ContentDialogButton.Close }.Themed(XamlRoot);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { await ViewModel.CancelAsync(row.Id, reason.Text); await ViewModel.SearchAsync(); }
        catch (Exception ex) { await ShowMessageAsync("Could not cancel", ex.Message); }
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
    private async Task ShowMessageAsync(string title, string message) => await new ContentDialog { Title = title, Content = message, CloseButtonText = "Close" }.Themed(XamlRoot).ShowAsync();
}
