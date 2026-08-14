using Kirana.Domain.Entities;
using Kirana.App.Printing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class PurchaseOrderDetailsDialog : ContentDialog
{
    public PurchaseOrder Order { get; }
    public bool ReconciliationRequested { get; private set; }
    public PurchaseOrderDetailsDialog(PurchaseOrder order) { Order = order; InitializeComponent(); DateText.Text = order.OrderDateUtc.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt"); if (order.Status is PurchaseOrderStatus.Submitted or PurchaseOrderStatus.PartiallyReceived) SecondaryButtonText = "Receive Goods"; ReconciliationButton.Visibility = order.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Cancelled ? Visibility.Collapsed : Visibility.Visible; PrimaryButtonClick += OnPrintClick; }
    private async void OnPrintClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        using var helper = new PurchaseOrderPrintHelper(App.MainWindow, Order);
        await helper.ShowAsync();
    }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
    private void OnReconciliationClick(object sender, RoutedEventArgs e) { ReconciliationRequested = true; Hide(); }
}
