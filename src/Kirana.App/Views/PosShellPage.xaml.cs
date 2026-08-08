using Kirana.App.Theming;
using Kirana.App.Services;
using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Printing;
using Kirana.Application.Hardware;
using Kirana.Application.Products;
using Kirana.Application.Promotions;
using Kirana.Application.Taxation;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class PosShellPage : Page
{
    public PosShellViewModel ViewModel { get; }
    public DeviceStatusViewModel DeviceStatus { get; }
    private readonly IScannerService _scannerService;
    private readonly IHardwareSettingsService _hardwareSettingsService;
    private readonly IReceiptHardwareGuard _receiptHardwareGuard;
    private readonly IHardwareMonitor _hardwareMonitor;
    private bool _scannerEnabled = true;
    private bool _scannerSoundEnabled = true;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    // Suggestions query fires ~180ms after the user stops typing rather than on every keystroke —
    // long enough that a real barcode scanner's whole burst-plus-Enter (a few ms per PRD §18) has
    // already been consumed by ScannerBuffer and the box cleared before this ever ticks.
    private readonly DispatcherTimer _suggestionDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };

    public PosShellPage()
    {
        var services = App.Services;
        _scannerService = services.GetRequiredService<IScannerService>();
        _hardwareSettingsService = services.GetRequiredService<IHardwareSettingsService>();
        _receiptHardwareGuard = services.GetRequiredService<IReceiptHardwareGuard>();
        _hardwareMonitor = services.GetRequiredService<IHardwareMonitor>();
        DeviceStatus = new DeviceStatusViewModel(
            _hardwareMonitor, _hardwareSettingsService);
        _hardwareMonitor.StatusChanged += OnHardwareStatusChanged;
        Unloaded += (_, _) =>
        {
            _hardwareMonitor.StatusChanged -= OnHardwareStatusChanged;
            _clockTimer.Stop();
        };
        ViewModel = new PosShellViewModel(
            services.GetRequiredService<IProductService>(),
            services.GetRequiredService<IBarcodeLookupService>(),
            services.GetRequiredService<IHeldBillService>(),
            services.GetRequiredService<ICustomerService>(),
            services.GetRequiredService<IPromotionEngine>(),
            services.GetRequiredService<IGstCalculationService>(),
            services.GetRequiredService<IKiranaDbContext>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        _clockTimer.Tick += (_, _) => UpdateClock();
        Loaded += async (_, _) =>
        {
            UpdateThemeIcon();
            UpdateClock();
            _clockTimer.Start();
            var hardwareSettings = new HardwareSettings();
            try
            {
                hardwareSettings = await _hardwareSettingsService.GetAsync();
            }
            catch (Exception ex)
            {
                HardwareWarningBar.Message = $"Hardware settings could not be loaded: {ex.Message}. Billing remains available.";
                HardwareWarningBar.IsOpen = true;
            }
            _scannerEnabled = hardwareSettings.BarcodeScannerEnabled;
            _scannerSoundEnabled = hardwareSettings.EnableSoundOnScan;
            ViewModel.ConfigureScannerTiming(hardwareSettings.ScannerTimeoutMilliseconds);
            ViewModel.ScannerBuffer.BarcodeScanned += OnScannerObserved;
            await ViewModel.InitializeAsync();
            await DeviceStatus.RefreshAsync();
            if (hardwareSettings.AutoFocusScannerInput) ScanSearchBox.Focus(FocusState.Programmatic);
            if (!_scannerEnabled)
            {
                HardwareWarningBar.Message = "Scanner input is disabled. Manual product search remains available.";
                HardwareWarningBar.IsOpen = true;
            }
        };

        _suggestionDebounce.Tick += async (_, _) =>
        {
            _suggestionDebounce.Stop();
            await ViewModel.UpdateSuggestionsAsync(ScanSearchBox.Text);
        };

        // Shortcuts are routed from a single tunnelling handler registered with
        // handledEventsToo, rather than from KeyboardAccelerator. Accelerators only fire when the
        // focused element sits in the same focus scope and hasn't already marked the key handled —
        // which is why F4 did nothing once focus moved into the cart's quantity/price/discount
        // TextBoxes or the suggestions Popup (a Popup is its own visual root, so a Page-level
        // accelerator never sees its keys at all). AddHandler(..., handledEventsToo: true) is the
        // pattern MainWindow already relies on for idle-timer activity tracking, and it sees the
        // key regardless of who handled it first.
        AddHandler(KeyDownEvent, new KeyEventHandler(OnShortcutKeyDown), handledEventsToo: true);
        SuggestionsPopup.AddHandler(KeyDownEvent, new KeyEventHandler(OnShortcutKeyDown), handledEventsToo: true);
    }

    /// <summary>Guards against opening a second modal on top of one that is already showing —
    /// WinUI throws when two ContentDialogs overlap, and it is also what the "except when a modal
    /// dialog is already open" rule requires.</summary>
    private bool _isModalOpen;

    private void OnShortcutKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // While a dialog is up it owns the keyboard; the shortcut must not fire underneath it.
        if (_isModalOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Windows.System.VirtualKey.F2:
                ScanSearchBox.Focus(FocusState.Programmatic);
                break;
            case Windows.System.VirtualKey.F4:
                _ = OpenCustomerPickerAsync();
                break;
            case Windows.System.VirtualKey.F6:
                _ = HoldCurrentBillAsync();
                break;
            case Windows.System.VirtualKey.F7:
                _ = OpenHeldBillsAsync();
                break;
            case Windows.System.VirtualKey.F8:
                _ = OpenBillDiscountAsync();
                break;
            case Windows.System.VirtualKey.F9:
                _ = OpenPaymentAsync();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Shows a dialog with the modal guard held, so shortcuts can't stack a second one
    /// on top of it. Every dialog on this page goes through here.</summary>
    private async Task<ContentDialogResult> ShowModalAsync(ContentDialog dialog)
    {
        if (_isModalOpen)
        {
            return ContentDialogResult.None;
        }

        _isModalOpen = true;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _isModalOpen = false;
        }
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

    private void UpdateClock()
    {
        var now = DateTime.Now;
        CurrentDayDateText.Text = now.ToString("dddd, dd MMM yyyy");
        CurrentTimeText.Text = now.ToString("hh:mm tt");
    }

    private async void OnDashboardClick(object sender, RoutedEventArgs e)
    {
        var authService = App.Services.GetRequiredService<IAuthenticationService>();
        var dialog = new ManagementLoginDialog(authService).Themed(XamlRoot);

        await ShowModalAsync(dialog);

        if (dialog.Unlocked)
        {
            Frame.Navigate(typeof(ManagementShellPage));
        }
    }

    // ===============================  BILLING TABS  ===============================

    private void OnNewBillClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddBill();
        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnBillTabClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is BillSessionViewModel bill)
        {
            ViewModel.SwitchToBill(bill);
            ScanSearchBox.Focus(FocusState.Programmatic);
        }
    }

    private async void OnCloseBillClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BillSessionViewModel bill)
        {
            return;
        }

        if (!ViewModel.CanCloseActiveBill)
        {
            return;
        }

        // Only interrupt the cashier when there is something to lose.
        if (ViewModel.BillHasItems(bill))
        {
            var confirm = new ContentDialog
            {
                Title = $"Close {bill.Title}?",
                Content = "This bill still has items in it. Closing it will discard them.",
                PrimaryButtonText = "Close bill",
                CloseButtonText = "Keep it",
                DefaultButton = ContentDialogButton.Close,
            };
            confirm.Themed(XamlRoot);

            if (await ShowModalAsync(confirm) != ContentDialogResult.Primary)
            {
                return;
            }
        }

        ViewModel.CloseBill(bill);
        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (_scannerEnabled) ViewModel.ScannerBuffer.OnCharacter(args.Character, DateTimeOffset.UtcNow);
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

        // A recognized scan has already been added to the cart via the buffer's BarcodeScanned
        // event — falling through to the manual search here would add the same product twice.
        var handledAsScan = _scannerEnabled && ViewModel.ScannerBuffer.OnEnterPressed(DateTimeOffset.UtcNow);

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
        await ShowModalAsync(dialog);

        if (dialog.Confirmed)
        {
            ViewModel.SelectedCustomer = dialog.SelectedCustomer;
            await ViewModel.RefreshPromotionsAsync();
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

        await ViewModel.RefreshPromotionsAsync();

        await ViewModel.HoldCurrentBillAsync();
        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private async void OnResumeClick(object sender, RoutedEventArgs e) => await OpenHeldBillsAsync();

    private async Task OpenHeldBillsAsync()
    {
        var dialog = new HeldBillsDialog(ViewModel).Themed(XamlRoot);
        await ShowModalAsync(dialog);

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
        var result = await ShowModalAsync(dialog);

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
        var result = await ShowModalAsync(dialog);

        if (result == ContentDialogResult.Primary && dialog.AuthorizedUserId is { } userId)
        {
            ViewModel.SetDiscountAuthorization(userId);
            return true;
        }

        return false;
    }

    private void OnClearBillDiscountClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearBillDiscount();
        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private async Task<bool> TryAuthorizePriceOverrideAsync()
    {
        var authService = App.Services.GetRequiredService<IAuthenticationService>();
        var dialog = new ManagerAuthorizationDialog(authService, PermissionKeys.PricingChangeSellingPrice).Themed(XamlRoot);
        var result = await ShowModalAsync(dialog);

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
        await ShowModalAsync(dialog);

        if (paymentViewModel.CompletedSale is { } sale)
        {
            App.Services.GetRequiredService<InvoiceRefreshNotifier>().NotifyInvoicesChanged();
            await ShowSaleCompletedDialogAsync(sale);
            ViewModel.ClearCart();
        }

        ScanSearchBox.Focus(FocusState.Programmatic);
    }

    private async Task ShowSaleCompletedDialogAsync(Sale sale)
    {
        var invoicePrintService = App.Services.GetRequiredService<IInvoicePrintService>();

        HardwareSettings hardwareSettings;
        try { hardwareSettings = await _hardwareSettingsService.GetAsync(); }
        catch { hardwareSettings = new HardwareSettings(); }
        var printerReadiness = await _receiptHardwareGuard.CheckAvailabilityAsync();
        if (!printerReadiness.Succeeded)
        {
            HardwareWarningBar.Message = $"Sale completed. {printerReadiness.Message} You can print the saved invoice later.";
            HardwareWarningBar.IsOpen = true;
        }

        try
        {
            var document = await invoicePrintService.GetInvoiceDocumentAsync(sale.Id);
            var previewViewModel = new InvoicePreviewViewModel(
                document, ViewModel.DefaultInvoiceFormat, ViewModel.CashierUserId, isReprint: false, invoicePrintService);

            var automaticCopies = printerReadiness.Succeeded && hardwareSettings.AutoPrintReceipt
                ? hardwareSettings.PrintDuplicateCopy ? 2 : 1
                : 0;
            var dialog = new InvoicePreviewDialog(previewViewModel, automaticCopies).Themed(XamlRoot);
            dialog.Title = $"Sale Completed — Invoice {sale.InvoiceNumber} — ₹{sale.GrandTotal:0.00}";
            await ShowModalAsync(dialog);
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
            await ShowModalAsync(errorDialog);
        }
    }

    private void OnScannerObserved(string barcode)
    {
        _scannerService.ReportSuccessfulScan(barcode);
        if (_scannerSoundEnabled) ElementSoundPlayer.Play(ElementSoundKind.Invoke);
        _ = DispatcherQueue.TryEnqueue(async () => await DeviceStatus.RefreshAsync());
    }

    private void OnHardwareStatusChanged(HardwareStatusChangedEventArgs change)
    {
        _ = DispatcherQueue.TryEnqueue(async () =>
        {
            HardwareWarningBar.Severity = change.CurrentStatus == HardwareStatus.Connected
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            HardwareWarningBar.Message = $"{change.Device.FriendlyName}: {change.CurrentStatus}.";
            HardwareWarningBar.IsOpen = true;
            await DeviceStatus.RefreshAsync();
        });
    }

    private async void OnHardwareStatusClick(object sender, RoutedEventArgs e)
    {
        var session = App.Services.GetRequiredService<ManagementSession>();
        if (session.CurrentUser is null)
        {
            var authService = App.Services.GetRequiredService<IAuthenticationService>();
            var dialog = new ManagementLoginDialog(authService).Themed(XamlRoot);
            await ShowModalAsync(dialog);
            if (!dialog.Unlocked) return;
        }

        Frame.Navigate(typeof(ManagementShellPage),
            session.HasPermission(PermissionKeys.HardwareManage) ? "HardwareSettings" : "DeviceStatus");
    }
}
