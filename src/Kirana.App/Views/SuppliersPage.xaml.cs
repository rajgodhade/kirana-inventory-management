using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class SuppliersPage : Page
{
    public SuppliersViewModel ViewModel { get; }

    public SuppliersPage()
    {
        var services = App.Services;
        ViewModel = new SuppliersViewModel(services.GetRequiredService<ISupplierService>(), services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ManagementPlaceholderPage));

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        App.Services.GetRequiredService<IAuthenticationService>().LockAndReturnToBilling();
        Frame.Navigate(typeof(PosShellPage));
    }

    private async void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await ViewModel.SearchAsync();
        }
    }

    private async void OnShowInactiveChanged(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();

    private async void OnAddSupplierClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases)
        {
            return;
        }

        var editViewModel = new SupplierEditViewModel(ViewModel, existingSupplier: null);
        var dialog = new SupplierEditDialog(editViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases || (sender as Button)?.Tag is not SupplierRowViewModel row)
        {
            return;
        }

        var supplier = await ViewModel.GetSupplierAsync(row.Id);
        if (supplier is null)
        {
            return;
        }

        var editViewModel = new SupplierEditViewModel(ViewModel, supplier);
        var dialog = new SupplierEditDialog(editViewModel) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnToggleActiveClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases || (sender as Button)?.Tag is not SupplierRowViewModel row)
        {
            return;
        }

        await ViewModel.SetActiveAsync(row.Id, !row.IsActive);
        await ViewModel.SearchAsync();
    }

    private void OnLedgerClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SupplierRowViewModel row)
        {
            return;
        }

        Frame.Navigate(typeof(SupplierLedgerPage), row.Id);
    }
}
