using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kirana.App.Views;

public sealed partial class SupplierLedgerPage : Page
{
    public SupplierLedgerViewModel ViewModel { get; private set; } = null!;

    public SupplierLedgerPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var supplierId = (int)e.Parameter;
        var services = App.Services;
        ViewModel = new SupplierLedgerViewModel(
            supplierId,
            services.GetRequiredService<ISupplierService>(),
            services.GetRequiredService<IPurchaseService>(),
            services.GetRequiredService<ManagementSession>());

        Bindings.Update();
        _ = ViewModel.InitializeAsync();
    }

    private async void OnRecordPaymentClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManagePurchases)
        {
            return;
        }

        var dialog = new SupplierPaymentDialog(ViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();
    }
}
