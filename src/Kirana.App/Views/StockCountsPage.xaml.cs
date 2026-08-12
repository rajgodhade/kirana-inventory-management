using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Products;
using Kirana.Application.StockCounts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class StockCountsPage : Page
{
    public StockCountViewModel ViewModel { get; }

    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public StockCountsPage()
    {
        ViewModel = new StockCountViewModel(
            App.Services.GetRequiredService<IStockCountService>(),
            App.Services.GetRequiredService<IProductService>(),
            App.Services.GetRequiredService<ManagementSession>());

        InitializeComponent();

        ViewModel.ScannerBuffer.BarcodeScanned += barcode => _ = ViewModel.ScanAsync(barcode);

        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ViewModel.SearchProductsAsync();
        };

        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    // ---- Scan / search box ----

    private void OnScanCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        ViewModel.ScannerBuffer.OnCharacter(args.Character, DateTimeOffset.UtcNow);
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void OnScanKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        _searchDebounce.Stop();

        // MUST check the return value before falling through to a manual search: when the buffer
        // recognised a scanner burst it has already raised BarcodeScanned and the product is being
        // added, so searching again on the same Enter would process the scan twice. Same guard as
        // PosShellPage and PurchaseEntryPage.
        var handledAsScan = ViewModel.ScannerBuffer.OnEnterPressed(DateTimeOffset.UtcNow);

        var text = ScanSearchBox.Text;
        ScanSearchBox.Text = string.Empty;

        if (!handledAsScan && !string.IsNullOrWhiteSpace(text))
        {
            ViewModel.SearchText = text;
            await ViewModel.SearchProductsAsync();
        }
    }

    // ---- Row actions ----

    private async void OnAddSearchResultClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is StockCountSearchRowViewModel row)
        {
            await ViewModel.AddProductAsync(row.Id);
        }
    }

    private async void OnQuantityLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is StockCountItemRowViewModel row)
        {
            await ViewModel.SetQuantityAsync(row);
        }
    }

    /// <summary>Enter commits the quantity without leaving the field, so a counter can type a
    /// number, press Enter, and move straight to the next scan.</summary>
    private async void OnQuantityKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        if ((sender as FrameworkElement)?.Tag is StockCountItemRowViewModel row)
        {
            await ViewModel.SetQuantityAsync(row);
        }
    }

    private async void OnRemoveItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is StockCountItemRowViewModel row)
        {
            await ViewModel.RemoveItemAsync(row);
        }
    }

    // ---- Destructive actions get an explicit confirmation ----

    private async void OnFinalizeClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Finalize stock count?",
            Content = "This will adjust inventory to match the physical quantities you counted. " +
                      "Stock movements will be recorded and the count can no longer be changed.",
            PrimaryButtonText = "Finalize",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.FinalizeCommand.ExecuteAsync(null);
        }
    }

    private async void OnCancelCountClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Cancel this stock count?",
            Content = "The count and everything recorded in it will be discarded. No stock will be changed.",
            PrimaryButtonText = "Cancel Count",
            CloseButtonText = "Keep Counting",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.CancelCountCommand.ExecuteAsync(null);
        }
    }
}
