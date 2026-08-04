using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class PurchasesPage : Page
{
    public PurchasesViewModel ViewModel { get; }

    public PurchasesPage()
    {
        var services = App.Services;
        ViewModel = new PurchasesViewModel(services.GetRequiredService<IPurchaseService>(), services.GetRequiredService<ManagementSession>());

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

    private async void OnFilterChanged(object sender, RoutedEventArgs e) => await ViewModel.SearchAsync();

    private void OnNewPurchaseClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases)
        {
            return;
        }

        Frame.Navigate(typeof(PurchaseEntryPage));
    }

    private async void OnDetailsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PurchaseRowViewModel row)
        {
            return;
        }

        var purchase = await ViewModel.GetPurchaseAsync(row.Id);
        if (purchase is null)
        {
            return;
        }

        var dialog = new PurchaseDetailsDialog(purchase) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }

    private async void OnPayClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases || (sender as Button)?.Tag is not PurchaseRowViewModel row)
        {
            return;
        }

        var purchase = await ViewModel.GetPurchaseAsync(row.Id);
        if (purchase is null)
        {
            return;
        }

        var dialog = new PurchasePaymentDialog(ViewModel, purchase.Id, purchase.SupplierId) { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }
}
