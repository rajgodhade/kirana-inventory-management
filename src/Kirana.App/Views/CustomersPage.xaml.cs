using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Customers;
using Kirana.Application.Printing;
using Kirana.App.Printing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Kirana.App.Views;

public sealed partial class CustomersPage : Page
{
    // Same live-as-you-type search this app uses everywhere else — fires ~300ms after the user
    // stops typing so the list doesn't re-query on every keystroke, and Enter/the Search button
    // still work immediately for anyone who prefers them.
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public CustomersViewModel ViewModel { get; }

    public CustomersPage()
    {
        var services = App.Services;
        ViewModel = new CustomersViewModel(
            services.GetRequiredService<ICustomerService>(),
            services.GetRequiredService<ICustomerCreditService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();

        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        };
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        }
    }

    private async void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        // Routed checked/selection events can arrive before the generated x:Bind setter. Read the
        // control directly so the screen never shows a checked filter with unfiltered rows.
        if (sender is CheckBox outstandingOnly)
        {
            ViewModel.OutstandingOnly = outstandingOnly.IsChecked == true;
        }
        else if (sender is ComboBox combo && combo.SelectedItem is string value)
        {
            if (combo == StatusFilterBox)
            {
                ViewModel.SelectedStatusFilter = value;
            }
            else if (combo == SortFilterBox)
            {
                ViewModel.SelectedSortOption = value;
            }
        }

        await ViewModel.SearchAsync();
    }

    private async void OnAllCustomersClick(object sender, RoutedEventArgs e) => await ViewModel.ShowAllAsync();

    private async void OnOutstandingCustomersClick(object sender, RoutedEventArgs e) => await ViewModel.ShowOutstandingAsync();

    private async void OnOverdueCustomersClick(object sender, RoutedEventArgs e) => await ViewModel.ShowOverdueAsync();

    private async void OnAddCustomerClick(object sender, RoutedEventArgs e)
    {
        var editViewModel = new CustomerEditViewModel(ViewModel, existingCustomer: null);
        var dialog = new CustomerEditDialog(editViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.InitializeAsync();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageCustomers || (sender as FrameworkElement)?.Tag is not CustomerRowViewModel row)
        {
            return;
        }

        var customer = await ViewModel.GetCustomerAsync(row.Id);
        if (customer is null)
        {
            return;
        }

        var editViewModel = new CustomerEditViewModel(ViewModel, customer);
        var dialog = new CustomerEditDialog(editViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.InitializeAsync();
    }

    private async void OnToggleActiveClick(object sender, RoutedEventArgs e)
    {
        // Raised from a MenuFlyoutItem in the row's overflow menu, not a Button.
        if ((sender as FrameworkElement)?.Tag is not CustomerRowViewModel row)
        {
            return;
        }

        try
        {
            await ViewModel.SetActiveAsync(row.Id, !row.IsActive);
        }
        catch (Exception ex)
        {
            // Deactivating a customer who still owes Udhaar is refused by the service.
            ViewModel.ErrorMessage = ex.Message;
        }

        await ViewModel.InitializeAsync();
    }

    private void OnLedgerClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CustomerRowViewModel row)
        {
            return;
        }

        Frame.Navigate(typeof(CustomerLedgerPage), row.Id);
    }

    private async void OnReceivePaymentClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageCustomers || (sender as FrameworkElement)?.Tag is not CustomerRowViewModel row)
        {
            return;
        }

        var services = App.Services;
        var ledgerViewModel = new CustomerLedgerViewModel(
            row.Id,
            services.GetRequiredService<ICustomerService>(),
            services.GetRequiredService<ICustomerCreditService>(),
            services.GetRequiredService<ManagementSession>());
        await ledgerViewModel.InitializeAsync();
        if (!ledgerViewModel.HasOutstanding)
        {
            return;
        }

        var dialog = new CreditPaymentDialog(ledgerViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();
        if (dialog.RecordedPayment is { } payment && dialog.ShouldPrintReceipt)
        {
            await PrintReceiptAsync(payment.Id);
        }

        await ViewModel.SearchAsync();
    }

    private void OnCustomerRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CustomerRowViewModel row || IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Frame.Navigate(typeof(CustomerLedgerPage), row.Id);
    }

    private async Task PrintReceiptAsync(int creditPaymentId)
    {
        try
        {
            var receiptService = App.Services.GetRequiredService<ICustomerReceiptService>();
            var document = await receiptService.GetReceiptAsync(creditPaymentId, ViewModel.CurrentUserId);
            using var helper = new CustomerReceiptPrintHelper(App.MainWindow, document, InvoiceFormat.Thermal80mm);
            await helper.ShowPrintUIAsync();
            await receiptService.LogPrintAsync(creditPaymentId, ViewModel.CurrentUserId);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Could not print the receipt: {ex.Message}. The repayment was saved.";
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Button)
            {
                return true;
            }
        }

        return false;
    }
}
