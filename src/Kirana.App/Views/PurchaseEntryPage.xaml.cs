using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Products;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class PurchaseEntryPage : Page
{
    private readonly DispatcherTimer _suggestionDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public PurchaseEntryViewModel ViewModel { get; }

    public PurchaseEntryPage()
    {
        var services = App.Services;
        ViewModel = new PurchaseEntryViewModel(
            services.GetRequiredService<IProductService>(),
            services.GetRequiredService<IBarcodeLookupService>(),
            services.GetRequiredService<ISupplierService>(),
            services.GetRequiredService<IPurchaseService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += (_, _) =>
        {
            ViewModel.Initialize();
            ScanSearchBox.Focus(FocusState.Programmatic);
        };

        _suggestionDebounce.Tick += async (_, _) =>
        {
            _suggestionDebounce.Stop();
            await ViewModel.UpdateSuggestionsAsync(ScanSearchBox.Text);
        };
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args) =>
        ViewModel.ScannerBuffer.OnCharacter(args.Character, DateTimeOffset.UtcNow);

    private void OnScanSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _suggestionDebounce.Stop();
        _suggestionDebounce.Start();
    }

    private async void OnScanSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _suggestionDebounce.Stop();
            ViewModel.ClearSuggestions();
            return;
        }

        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        _suggestionDebounce.Stop();
        ViewModel.ClearSuggestions();

        // A recognized scan has already been added via the buffer's BarcodeScanned event — falling
        // through to the manual search here would receive the same product twice.
        var handledAsScan = ViewModel.ScannerBuffer.OnEnterPressed(DateTimeOffset.UtcNow);

        if (handledAsScan)
        {
            ScanSearchBox.Text = string.Empty;
            return;
        }

        await AddCurrentSearchTextAsync();
    }

    private async void OnAddItemClick(object sender, RoutedEventArgs e) => await AddCurrentSearchTextAsync();

    private async Task AddCurrentSearchTextAsync()
    {
        _suggestionDebounce.Stop();
        ViewModel.ClearSuggestions();

        var text = ScanSearchBox.Text;
        ScanSearchBox.Text = string.Empty;

        if (!string.IsNullOrWhiteSpace(text))
        {
            await ViewModel.HandleManualSearchAsync(text);
        }

        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnSuggestionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Product product)
        {
            return;
        }

        _suggestionDebounce.Stop();
        ViewModel.ClearSuggestions();
        ViewModel.AddOrIncrement(product);

        ScanSearchBox.Text = string.Empty;
        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnSupplierClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SupplierPickerDialog(ViewModel).Themed(XamlRoot);
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.Confirmed && dialog.SelectedSupplier is { } supplier)
        {
            ViewModel.SelectedSupplier = supplier;
        }

        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnRemoveLineClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is PurchaseLineViewModel line)
        {
            ViewModel.RemoveLine(line);
        }
    }

    private void OnLineFieldLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as TextBox)?.Tag is PurchaseLineViewModel line)
        {
            if (line.Quantity <= 0 || (!line.SupportsDecimalQuantity && line.Quantity != Math.Floor(line.Quantity)))
            {
                line.QuantityText = "1";
            }

            if (line.UnitPrice < 0)
            {
                line.UnitPriceText = "0";
            }

            if (line.DiscountPercent is < 0 or > 100)
            {
                line.DiscountPercentText = "0";
            }

            ViewModel.RecalculateTotals();
        }
    }

    private async void OnCompletePurchaseClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.CompletePurchaseCommand.ExecuteAsync(null);

        if (ViewModel.CompletedPurchase is { } purchase)
        {
            var dialog = new ContentDialog
            {
                Title = "Purchase Completed",
                Content = $"Purchase {purchase.PurchaseNumber}\nTotal: ₹{purchase.GrandTotal:0.00}\nOutstanding: ₹{purchase.OutstandingAmount:0.00}",
                CloseButtonText = "OK",
            };
            dialog.Themed(XamlRoot);
            await dialog.ShowAsync();

            Frame.Navigate(typeof(PurchasesPage));
        }
    }
}
