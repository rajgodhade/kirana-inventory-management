using Kirana.App.ViewModels;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

/// <summary>Read-only view of a finalized purchase — no further actions, so this dialog never
/// needs to open another one (avoids the nested-ContentDialog crash).</summary>
public sealed partial class PurchaseDetailsDialog : ContentDialog
{
    public PurchaseDetailsViewModel ViewModel { get; }

    public PurchaseDetailsDialog(Purchase purchase)
    {
        ViewModel = PurchaseDetailsViewModel.FromPurchase(purchase);
        InitializeComponent();
        if (purchase.PurchaseOrderId is not null)
        {
            SecondaryButtonText = "Reconciliation";
        }
    }

    private void OnCloseIconClick(object sender, RoutedEventArgs e) => Hide();
}
