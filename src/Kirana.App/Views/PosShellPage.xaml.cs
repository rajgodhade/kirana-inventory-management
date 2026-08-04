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
            await ViewModel.InitializeAsync();
            ScanSearchBox.Focus(FocusState.Programmatic);
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

    private async void OnDashboardClick(object sender, RoutedEventArgs e)
    {
        var authService = App.Services.GetRequiredService<IAuthenticationService>();
        var dialog = new ManagementLoginDialog(authService) { XamlRoot = XamlRoot };

        await dialog.ShowAsync();

        if (dialog.Unlocked)
        {
            Frame.Navigate(typeof(ManagementPlaceholderPage));
        }
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args) =>
        ViewModel.ScannerBuffer.OnCharacter(args.Character, DateTimeOffset.UtcNow);

    private async void OnScanSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

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

    private async void OnCustomerClick(object sender, RoutedEventArgs e) => await OpenCustomerPickerAsync();

    private async Task OpenCustomerPickerAsync()
    {
        var dialog = new CustomerPickerDialog(ViewModel) { XamlRoot = XamlRoot };
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
        var dialog = new HeldBillsDialog(ViewModel) { XamlRoot = XamlRoot };
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
        var dialog = new BillDiscountDialog(ViewModel.BillDiscountPercent) { XamlRoot = XamlRoot };
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
        var dialog = new ManagerAuthorizationDialog(authService, PermissionKeys.BillingApproveLargeDiscount) { XamlRoot = XamlRoot };
        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && dialog.AuthorizedUserId is { } userId)
        {
            ViewModel.SetDiscountAuthorization(userId);
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
        var dialog = new PaymentDialog(paymentViewModel) { XamlRoot = XamlRoot };
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

            var dialog = new InvoicePreviewDialog(previewViewModel) { XamlRoot = XamlRoot };
            dialog.Title = $"Sale Completed — Invoice {sale.InvoiceNumber} — ₹{sale.GrandTotal:0.00}";
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // The sale is already committed regardless of whether the invoice can be
            // previewed/printed — surface the problem but never block "next customer".
            var errorDialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Sale Completed",
                Content = $"Invoice {sale.InvoiceNumber}\nTotal: ₹{sale.GrandTotal:0.00}\n\nCouldn't prepare the invoice for printing: {ex.Message}",
                CloseButtonText = "Next Customer",
                DefaultButton = ContentDialogButton.Close,
            };
            await errorDialog.ShowAsync();
        }
    }
}
