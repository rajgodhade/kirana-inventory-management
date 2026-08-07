using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Kirana.App.Views;

public sealed partial class SuppliersPage : Page
{
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public SuppliersViewModel ViewModel { get; }

    public SuppliersPage()
    {
        var services = App.Services;
        ViewModel = new SuppliersViewModel(services.GetRequiredService<ISupplierService>(), services.GetRequiredService<ManagementSession>());

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

    private async void OnFilterChanged(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();

    private async void OnAllSuppliersClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OutstandingOnly = false;
        ViewModel.SelectedStatusFilter = "All suppliers";
        await ViewModel.SearchAsync();
    }

    private async void OnActiveSuppliersClick(object sender, RoutedEventArgs e) => await ViewModel.ShowActiveAsync();

    private async void OnOutstandingSuppliersClick(object sender, RoutedEventArgs e) => await ViewModel.ShowOutstandingAsync();

    private async void OnOverdueSuppliersClick(object sender, RoutedEventArgs e) => await ViewModel.ShowOverdueAsync();

    private async void OnAddSupplierClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases)
        {
            return;
        }

        var editViewModel = new SupplierEditViewModel(ViewModel, existingSupplier: null);
        var dialog = new SupplierEditDialog(editViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases || (sender as FrameworkElement)?.Tag is not SupplierRowViewModel row)
        {
            return;
        }

        var supplier = await ViewModel.GetSupplierAsync(row.Id);
        if (supplier is null)
        {
            return;
        }

        var editViewModel = new SupplierEditViewModel(ViewModel, supplier);
        var dialog = new SupplierEditDialog(editViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnToggleActiveClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases || (sender as FrameworkElement)?.Tag is not SupplierRowViewModel row)
        {
            return;
        }

        await ViewModel.SetActiveAsync(row.Id, !row.IsActive);
        await ViewModel.SearchAsync();
    }

    private void OnLedgerClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SupplierRowViewModel row)
        {
            return;
        }

        Frame.Navigate(typeof(SupplierLedgerPage), row.Id);
    }

    private async void OnPayClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases || (sender as FrameworkElement)?.Tag is not SupplierRowViewModel row)
        {
            return;
        }

        var services = App.Services;
        var paymentViewModel = new SupplierLedgerViewModel(
            row.Id,
            services.GetRequiredService<ISupplierService>(),
            services.GetRequiredService<IPurchaseService>(),
            services.GetRequiredService<ManagementSession>());
        var dialog = new SupplierPaymentDialog(paymentViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private void OnSupplierRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SupplierRowViewModel row || IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Frame.Navigate(typeof(SupplierLedgerPage), row.Id);
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
