using Kirana.Domain.Entities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class GoodsReceiptDetailsDialog : ContentDialog
{
    public GoodsReceipt Receipt { get; }
    public bool PurchaseOrderRequested { get; private set; }
    public bool PurchaseRequested { get; private set; }
    public bool ReconciliationRequested { get; private set; }
    public GoodsReceiptDetailsDialog(GoodsReceipt receipt) { Receipt = receipt; InitializeComponent(); ReceivedDateText.Text = receipt.ReceivedAtUtc.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt"); ViewPurchaseButton.Visibility = receipt.Purchase is null ? Visibility.Collapsed : Visibility.Visible; }
    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
    private void OnViewPurchaseOrderClick(object sender, RoutedEventArgs e) { PurchaseOrderRequested = true; Hide(); }
    private void OnViewPurchaseClick(object sender, RoutedEventArgs e) { PurchaseRequested = true; Hide(); }
    private void OnReconciliationClick(object sender, RoutedEventArgs e) { ReconciliationRequested = true; Hide(); }
}
