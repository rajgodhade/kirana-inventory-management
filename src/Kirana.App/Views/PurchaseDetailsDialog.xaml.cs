using Kirana.Domain.Entities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

/// <summary>Read-only view of a finalized purchase — no further actions, so this dialog never
/// needs to open another one (avoids the nested-ContentDialog crash).</summary>
public sealed partial class PurchaseDetailsDialog : ContentDialog
{
    public PurchaseDetailsDialog(Purchase purchase)
    {
        InitializeComponent();
        Title = $"Purchase {purchase.PurchaseNumber}";

        HeaderText.Text =
            $"Supplier: {purchase.Supplier.Name} ({purchase.Supplier.SupplierCode})\n" +
            $"Date: {purchase.PurchaseDateUtc.ToLocalTime():dd-MMM-yyyy hh:mm tt}\n" +
            (string.IsNullOrWhiteSpace(purchase.SupplierInvoiceNumber) ? "" : $"Supplier Invoice #: {purchase.SupplierInvoiceNumber}\n") +
            (string.IsNullOrWhiteSpace(purchase.Notes) ? "" : $"Notes: {purchase.Notes}\n");

        foreach (var item in purchase.Items)
        {
            var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            var name = new TextBlock { Text = item.ProductNameSnapshot, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(name, 0);
            var qty = new TextBlock { Text = item.Quantity.ToString("0.###") };
            Grid.SetColumn(qty, 1);
            var price = new TextBlock { Text = $"₹{item.PurchasePriceSnapshot:0.##}" };
            Grid.SetColumn(price, 2);
            var total = new TextBlock { Text = $"₹{item.LineTotal:0.00}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Grid.SetColumn(total, 3);

            grid.Children.Add(name);
            grid.Children.Add(qty);
            grid.Children.Add(price);
            grid.Children.Add(total);

            ItemsList.Items.Add(grid);
        }

        TotalsText.Text =
            $"Subtotal: ₹{purchase.SubTotal:0.00}\n" +
            $"Discount: ₹{purchase.DiscountTotal:0.00}\n" +
            $"GST: ₹{purchase.TaxTotal:0.00}\n" +
            $"Round Off: ₹{purchase.RoundOffAmount:0.00}\n" +
            $"Grand Total: ₹{purchase.GrandTotal:0.00}\n" +
            $"Amount Paid: ₹{purchase.AmountPaid:0.00}\n" +
            $"Outstanding: ₹{purchase.OutstandingAmount:0.00}";
    }
}
