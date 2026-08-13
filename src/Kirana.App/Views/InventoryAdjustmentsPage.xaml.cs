using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Inventories;
using Kirana.Application.Products;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class InventoryAdjustmentsPage : Page
{
    public InventoryAdjustmentViewModel ViewModel { get; }

    private readonly DispatcherTimer _productSearchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _historySearchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public InventoryAdjustmentsPage()
    {
        ViewModel = new InventoryAdjustmentViewModel(
            App.Services.GetRequiredService<IInventoryAdjustmentService>(),
            App.Services.GetRequiredService<IProductService>(),
            App.Services.GetRequiredService<IBarcodeLookupService>(),
            App.Services.GetRequiredService<ManagementSession>());

        InitializeComponent();

        ViewModel.ScannerBuffer.BarcodeScanned += barcode => _ = ViewModel.ScanAsync(barcode);

        _productSearchDebounce.Tick += async (_, _) =>
        {
            _productSearchDebounce.Stop();
            await ViewModel.SearchProductsAsync();
        };

        _historySearchDebounce.Tick += async (_, _) =>
        {
            _historySearchDebounce.Stop();
            await ViewModel.LoadAsync();
        };

        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    /// <summary>
    /// An int parameter preselects that product and opens straight into the create form — this is
    /// how the Products page's "Stock" action arrives, so adjusting a specific product stays one
    /// click away without a second, weaker adjustment path existing.
    /// </summary>
    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is int productId)
        {
            ViewModel.StartNewCommand.Execute(null);
            await ViewModel.SelectProductAsync(productId);
        }
    }

    // ---- History filters ----

    private void OnHistorySearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _historySearchDebounce.Stop();
        _historySearchDebounce.Start();
    }

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        // The x:Bind TwoWay update and this handler both fire off the same event, so the bound
        // property can still hold the previous value here. ComboBox exposes the new selection on the
        // event args, which is the reliable source — the same class of trap documented for the
        // checkbox filters on the Products page.
        if (e.AddedItems.Count > 0 && sender is ComboBox combo && combo.SelectedItem is string selected)
        {
            if (ViewModel.ReasonFilterOptions.Contains(selected))
            {
                ViewModel.SelectedReasonFilter = selected;
            }
            else if (ViewModel.DirectionFilterOptions.Contains(selected))
            {
                ViewModel.SelectedDirectionFilter = selected;
            }
        }

        await ViewModel.LoadAsync();
    }

    private async void OnDateFilterChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        await ViewModel.LoadAsync();

    // ---- Product selection ----

    private void OnScanCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        ViewModel.ScannerBuffer.OnCharacter(args.Character, DateTimeOffset.UtcNow);
        _productSearchDebounce.Stop();
        _productSearchDebounce.Start();
    }

    private async void OnScanKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        _productSearchDebounce.Stop();

        // MUST check the return value before falling through to a manual search: when the buffer
        // recognised a scanner burst it has already raised BarcodeScanned and selected the product,
        // so searching again on the same Enter would process the scan twice. Same guard as
        // PosShellPage, PurchaseEntryPage and StockCountsPage.
        var handledAsScan = ViewModel.ScannerBuffer.OnEnterPressed(DateTimeOffset.UtcNow);

        var text = ProductScanBox.Text;

        if (!handledAsScan && !string.IsNullOrWhiteSpace(text))
        {
            ViewModel.ProductSearchText = text;
            await ViewModel.SearchProductsAsync();
        }
    }

    /// <summary>Clears the chosen product so the search box reappears. Deliberately does not reset
    /// the quantity/reason the operator has already typed — only the product changes.</summary>
    private void OnChangeProductClick(object sender, RoutedEventArgs e) => ViewModel.ClearSelectedProduct();

    private async void OnSelectProductClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is AdjustmentProductRowViewModel row)
        {
            await ViewModel.SelectProductAsync(row.Id);
        }
    }

    /// <summary>The one place inventory is mutated, behind an explicit confirmation — an adjustment
    /// cannot be edited or undone, so it must never happen on a stray click.</summary>
    private async void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Confirm inventory adjustment?",
            Content = $"{ViewModel.SelectedProductName}\n\n" +
                      $"{ViewModel.PreviewTransitionText}  ({ViewModel.SignedQuantityText})\n" +
                      $"Reason: {ViewModel.SelectedReason}\n\n" +
                      "This will change stock immediately and cannot be edited or deleted. " +
                      "A mistake can only be corrected with another adjustment.",
            PrimaryButtonText = "Confirm Adjustment",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ConfirmCommand.ExecuteAsync(null);
        }
    }
}
