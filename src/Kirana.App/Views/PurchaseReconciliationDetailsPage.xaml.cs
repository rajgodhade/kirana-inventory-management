using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kirana.App.Views;

public sealed partial class PurchaseReconciliationDetailsPage : Page
{
    private readonly IPurchaseOrderService _purchaseOrders;
    private readonly IGoodsReceiptService _goodsReceipts;
    private readonly IPurchaseService _purchases;
    private readonly ManagementSession _session;
    private int _purchaseOrderId;
    public PurchaseReconciliationDetailsViewModel ViewModel { get; }

    public PurchaseReconciliationDetailsPage()
    {
        var services = App.Services;
        _session = services.GetRequiredService<ManagementSession>();
        _purchaseOrders = services.GetRequiredService<IPurchaseOrderService>();
        _goodsReceipts = services.GetRequiredService<IGoodsReceiptService>();
        _purchases = services.GetRequiredService<IPurchaseService>();
        ViewModel = new(services.GetRequiredService<IPurchaseReconciliationService>(), _session);
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int id)
        {
            _purchaseOrderId = id;
            await ViewModel.LoadAsync(id);
        }
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync(_purchaseOrderId);

    private async void OnViewPurchaseOrderClick(object sender, RoutedEventArgs e)
    {
        var order = await _purchaseOrders.GetByIdAsync(_purchaseOrderId, _session.CurrentUser?.Id);
        if (order is null) return;
        var result = await new PurchaseOrderDetailsDialog(order).Themed(XamlRoot).ShowAsync();
        if (result == ContentDialogResult.Secondary)
            Frame.Navigate(typeof(GoodsReceiptEntryPage), order.Id);
    }

    private async void OnViewDocumentClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PurchaseReconciliationDocumentViewModel document) return;
        if (document.Type == "GRN")
        {
            var receipt = await _goodsReceipts.GetByIdAsync(document.Id, _session.CurrentUser?.Id);
            if (receipt is not null) await new GoodsReceiptDetailsDialog(receipt).Themed(XamlRoot).ShowAsync();
        }
        else
        {
            var purchase = await _purchases.GetByIdAsync(document.Id, _session.CurrentUser?.Id);
            if (purchase is not null) await new PurchaseDetailsDialog(purchase).Themed(XamlRoot).ShowAsync();
        }
    }
}
