using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Customers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

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

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        _searchDebounce.Stop();
        await ViewModel.SearchAsync();
    }

    private async void OnFilterChanged(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();

    private async void OnAddCustomerClick(object sender, RoutedEventArgs e)
    {
        var editViewModel = new CustomerEditViewModel(ViewModel, existingCustomer: null);
        var dialog = new CustomerEditDialog(editViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.InitializeAsync();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CustomerRowViewModel row)
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
        if ((sender as Button)?.Tag is not CustomerRowViewModel row)
        {
            return;
        }

        Frame.Navigate(typeof(CustomerLedgerPage), row.Id);
    }
}
