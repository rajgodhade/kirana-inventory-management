using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Printing;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class PosShellPage : Page
{
    public PosShellViewModel ViewModel { get; }

    // Suggestions query fires ~180ms after the user stops typing rather than on every keystroke —
    // long enough that a real barcode scanner's whole burst-plus-Enter (a few ms per PRD §18) has
    // already been consumed by ScannerBuffer and the box cleared before this ever ticks.
    private readonly DispatcherTimer _suggestionDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public PosShellPage()
    {
        var services = App.Services;
        ViewModel = new PosShellViewModel(
            services.GetRequiredService<IProductService>(),
            services.GetRequiredService<IBarcodeLookupService>(),
            services.GetRequiredService<IHeldBillService>(),
            services.GetRequiredService<ICustomerService>(),
            services.GetRequiredService<IKiranaDbContext>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) =>
        {
            UpdateThemeIcon();
            await ViewModel.InitializeAsync();
            ScanSearchBox.Focus(FocusState.Programmatic);
        };

        _suggestionDebounce.Tick += async (_, _) =>
        {
            _suggestionDebounce.Stop();
            await ViewModel.UpdateSuggestionsAsync(ScanSearchBox.Text);
        };

        AddShortcut(Windows.System.VirtualKey.F2, () => ScanSearchBox.Focus(FocusState.Programmatic));
        AddShortcut(Windows.System.VirtualKey.F4, () => _ = OpenCustomerPickerAsync());
        AddShortcut(Windows.System.VirtualKey.F6, () => _ = HoldCurrentBillAsync());
        AddShortcut(Windows.System.VirtualKey.F7, () => _ = OpenHeldBillsAsync());
        AddShortcut(Windows.System.VirtualKey.F8, () => _ = OpenBillDiscountAsync());
        AddShortcut(Windows.System.VirtualKey.F9, () => _ = OpenPaymentAsync());
    }

    private void AddShortcut(Windows.System.VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        KeyboardAccelerators.Add(accelerator);
    }

    /// <summary>Theme toggle is available on the billing screen too — a cashier who never opens
    /// the Dashboard should still be able to switch to dark in a dim shop.</summary>
    private async void OnThemeToggleClick(object sender, RoutedEventArgs e)
    {
        var themeService = App.Services.GetRequiredService<ThemeService>();
        await themeService.ToggleAsync();
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        var themeService = App.Services.GetRequiredService<ThemeService>();
        //  = Brightness (sun),  = Quiet Hours (moon) in Segoe MDL2 Assets.
        ThemeIcon.Glyph = themeService.IsEffectivelyDark ? "" : "";
        ToolTipService.SetToolTip(ThemeIcon, themeService.IsEffectivelyDark ? "Switch to light" : "Switch to dark");
    }

    private async void OnDashboardClick(object sender, RoutedEventArgs e)
    {
        var authService = App.Services.GetRequiredService<IAuthenticationService>();
        var dialog = new ManagementLoginDialog(authService).Themed(XamlRoot);

        await dialog.ShowAsync();

        if (dialog.Unlocked)
        {
            Frame.Navigate(typeof(ManagementShellPage));
        }
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args) =>
        ViewModel.ScannerBuffer.OnCharacter(args.Character, DateTimeOffset.UtcNow);

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

        // A recognized scan has already been added to the cart via the buffer's BarcodeScanned
        // event — falling through to the manual search here would add the same product twice.
        var handledAsScan = ViewModel.ScannerBuffer.OnEnterPressed(DateTimeOffset.UtcNow);

        var text = ScanSearchBox.Text;
        ScanSearchBox.Text = string.Empty;

        if (!handledAsScan && !string.IsNullOrWhiteSpace(text))
        {
            await ViewModel.HandleManualSearchAsync(text);
        }
    }

    private void OnScanSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _suggestionDebounce.Stop();
        _suggestionDebounce.Start();
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

    private async void OnCustomerClick(object sender, RoutedEventArgs e) => await OpenCustomerPickerAsync();

    private async Task OpenCustomerPickerAsync()
    {
        var dialog = new CustomerPickerDialog(ViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();

        if (dialog.Confirmed)
        {
            ViewModel.SelectedCustomer = dialog.SelectedCustomer;
        }

        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnRemoveLineClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is CartLineViewModel line)
        {
            ViewModel.RemoveLine(line);
        }
    }

    // Live pricing feedback as the cashier types — no waiting for Tab/click-away. LostFocus still
    // owns clamping invalid values and prompting for manager authorization on a large discount, so
    // those checks don't fire mid-keystroke on a still-incomplete number.
    private void OnQuantityTextChanged(object sender, TextChangedEventArgs e)
    {
        if ((sender as TextBox)?.Tag is CartLineViewModel)
        {
            ViewModel.RecalculateCart();
        }
    }

    private void OnDiscountTextChanged(object sender, TextChangedEventArgs e)
    {
        if ((sender as TextBox)?.Tag is CartLineViewModel)
        {
            ViewModel.RecalculateCart();
        }
    }

    private void OnQuantityLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as TextBox)?.Tag is CartLineViewModel line)
        {
            if (line.Quantity <= 0 || (!line.SupportsDecimalQuantity && line.Quantity != Math.Floor(line.Quantity)))
            {
                line.QuantityText = "1";
            }

            ViewModel.RecalculateCart();
        }
    }

    private async void OnDiscountLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as TextBox)?.Tag is not CartLineViewModel line)
        {
            return;
        }

        var percent = line.DiscountPercent;
        if (percent < 0 || percent > 100)
        {
            line.DiscountPercentText = "0";
            ViewModel.RecalculateCart();
            return;
        }

        if (ViewModel.NeedsDiscountAuthorization(percent) && !await TryAuthorizeAsync())
        {
            line.DiscountPercentText = "0";
        }

        ViewModel.RecalculateCart();
    }

    private void OnPriceTextChanged(object sender, TextChangedEventArgs e)
    {
        if ((sender as TextBox)?.Tag is CartLineViewModel)
        {
            ViewModel.RecalculateCart();
        }
    }

    private async void OnPriceLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as TextBox)?.Tag is not CartLineViewModel line)
        {
            return;
        }

        if (!decimal.TryParse(line.UnitPriceText, out var price) || price < 0)
        {
            line.UnitPriceText = line.OriginalUnitPrice.ToString("0.##");
            ViewModel.RecalculateCart();
            return;
        }

        if (ViewModel.NeedsPriceOverrideAuthorization(line) && !await TryAuthorizePriceOverrideAsync())
        {
            line.UnitPriceText = line.OriginalUnitPrice.ToString("0.##");
        }

        ViewModel.RecalculateCart();
    }

    private async void OnHoldClick(object sender, RoutedEventArgs e) => await HoldCurrentBillAsync();

    private async Task HoldCurrentBillAsync()
    {
        if (ViewModel.CartLines.Count == 0)
        {
            return;
        }

        await ViewModel.HoldCurrentBillAsync();
        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnResumeClick(object sender, RoutedEventArgs e) => await OpenHeldBillsAsync();

    private async Task OpenHeldBillsAsync()
    {
        var dialog = new HeldBillsDialog(ViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();

        if (dialog.ResumedHeldBillId is { } heldBillId)
        {
            await ViewModel.ResumeHeldBillAsync(heldBillId);
        }

        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnDiscountClick(object sender, RoutedEventArgs e) => await OpenBillDiscountAsync();

    private async Task OpenBillDiscountAsync()
    {
        var dialog = new BillDiscountDialog(ViewModel.BillDiscountPercent).Themed(XamlRoot);
        var result = await dialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        if (ViewModel.NeedsDiscountAuthorization(dialog.Percent) && !await TryAuthorizeAsync())
        {
            ScanSearchBox.Focus(FocusState.Programmatic);
            return;
        }

        ViewModel.SetBillDiscount(dialog.Percent);
        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private async Task<bool> TryAuthorizeAsync()
    {
        var authService = App.Services.GetRequiredService<IAuthenticationService>();
        var dialog = new ManagerAuthorizationDialog(authService, PermissionKeys.BillingApproveLargeDiscount).Themed(XamlRoot);
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.AuthorizedUserId is { } userId)
        {
            ViewModel.SetDiscountAuthorization(userId);
            return true;
        }

        return false;
    }

    private async Task<bool> TryAuthorizePriceOverrideAsync()
    {
        var authService = App.Services.GetRequiredService<IAuthenticationService>();
        var dialog = new ManagerAuthorizationDialog(authService, PermissionKeys.PricingChangeSellingPrice).Themed(XamlRoot);
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.AuthorizedUserId is { } userId)
        {
            ViewModel.SetPriceOverrideAuthorization(userId);
            return true;
        }

        return false;
    }

    private async void OnPaymentClick(object sender, RoutedEventArgs e) => await OpenPaymentAsync();

    private async Task OpenPaymentAsync()
    {
        if (ViewModel.CartLines.Count == 0)
        {
            return;
        }

        var paymentViewModel = new PaymentViewModel(ViewModel, App.Services.GetRequiredService<ISaleService>());
        var dialog = new PaymentDialog(paymentViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();

        if (paymentViewModel.CompletedSale is { } sale)
        {
            await ShowSaleCompletedDialogAsync(sale);
            ViewModel.ClearCart();
        }

        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private async Task ShowSaleCompletedDialogAsync(Sale sale)
    {
        var invoicePrintService = App.Services.GetRequiredService<IInvoicePrintService>();

        try
        {
            var document = await invoicePrintService.GetInvoiceDocumentAsync(sale.Id);
            var previewViewModel = new InvoicePreviewViewModel(
                document, ViewModel.DefaultInvoiceFormat, ViewModel.CashierUserId, isReprint: false, invoicePrintService);

            var dialog = new InvoicePreviewDialog(previewViewModel).Themed(XamlRoot);
            dialog.Title = $"Sale Completed — Invoice {sale.InvoiceNumber} — ₹{sale.GrandTotal:0.00}";
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // The sale is already committed regardless of whether the invoice can be
            // previewed/printed — surface the problem but never block "next customer".
            var errorDialog = new ContentDialog
            {
                Title = "Sale Completed",
                Content = $"Invoice {sale.InvoiceNumber}\nTotal: ₹{sale.GrandTotal:0.00}\n\nCouldn't prepare the invoice for printing: {ex.Message}",
                CloseButtonText = "Next Customer",
                DefaultButton = ContentDialogButton.Close,
            };
            errorDialog.Themed(XamlRoot);
            await errorDialog.ShowAsync();
        }
    }
}
