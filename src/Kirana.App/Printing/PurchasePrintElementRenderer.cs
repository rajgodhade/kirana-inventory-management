using Kirana.Domain.Entities;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Printing;

/// <summary>
/// Renders a finalized purchase as WinUI elements for printing — an internal goods-received record
/// for the shopkeeper's own files, not a supplier-facing document. Mirrors
/// <see cref="CustomerReceiptElementRenderer"/>'s approach: plain default-brush elements (the print
/// pipeline renders onto a white page, not the app's theme), single page, no pagination needed since
/// a purchase's item list is already bounded by what fits in <c>PurchaseEntryPage</c>.
/// </summary>
public static class PurchasePrintElementRenderer
{
    public static FrameworkElement BuildElement(Purchase purchase, double widthDip)
    {
        var stack = new StackPanel { Width = widthDip, Padding = new Thickness(32), Spacing = 6 };

        stack.Children.Add(new TextBlock
        {
            Text = $"Purchase {purchase.PurchaseNumber}",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
        });

        var supplierName = purchase.GstIdentitySnapshotCapturedAtUtc is not null
            ? purchase.SupplierNameSnapshot ?? "Supplier"
            : purchase.Supplier.Name;
        var supplierCode = purchase.GstIdentitySnapshotCapturedAtUtc is not null
            ? purchase.SupplierCodeSnapshot
            : purchase.Supplier.SupplierCode;
        AddKeyValue(stack, "Supplier", string.IsNullOrWhiteSpace(supplierCode) ? supplierName : $"{supplierName} ({supplierCode})");
        if (purchase.GstIdentitySnapshotCapturedAtUtc is not null)
        {
            if (!string.IsNullOrWhiteSpace(purchase.SupplierGstinSnapshot))
                AddKeyValue(stack, "Supplier GSTIN", purchase.SupplierGstinSnapshot);
            if (!string.IsNullOrWhiteSpace(purchase.SupplierAddressSnapshot))
                AddKeyValue(stack, "Supplier Address", purchase.SupplierAddressSnapshot);
            var state = string.Join(" - ", new[] { purchase.SupplierStateCodeSnapshot, purchase.SupplierStateNameSnapshot }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            if (state.Length > 0) AddKeyValue(stack, "Supplier State", state);
        }
        AddKeyValue(stack, "Date", purchase.PurchaseDateUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"));

        if (!string.IsNullOrWhiteSpace(purchase.SupplierInvoiceNumber))
        {
            AddKeyValue(stack, "Supplier Invoice #", purchase.SupplierInvoiceNumber!);
        }

        AddSeparator(stack);

        var header = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 4, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        AddHeaderCell(header, "Product", 0);
        AddHeaderCell(header, "Qty", 1);
        AddHeaderCell(header, "Price", 2);
        AddHeaderCell(header, "Total", 3);
        stack.Children.Add(header);
        AddSeparator(stack);

        foreach (var item in purchase.Items)
        {
            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            var name = new TextBlock { Text = item.ProductNameSnapshot, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(name, 0);
            var qty = new TextBlock { Text = item.Quantity.ToString("0.###") };
            Grid.SetColumn(qty, 1);
            var price = new TextBlock { Text = $"₹{item.PurchasePriceSnapshot:0.##}" };
            Grid.SetColumn(price, 2);
            var total = new TextBlock { Text = $"₹{item.LineTotal:0.00}", FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(total, 3);

            row.Children.Add(name);
            row.Children.Add(qty);
            row.Children.Add(price);
            row.Children.Add(total);
            stack.Children.Add(row);
        }

        AddSeparator(stack);
        AddAmountRow(stack, "Subtotal", purchase.SubTotal);
        AddAmountRow(stack, "Discount", purchase.DiscountTotal);
        AddAmountRow(stack, "GST", purchase.TaxTotal);
        AddAmountRow(stack, "Round Off", purchase.RoundOffAmount);
        AddAmountRow(stack, "Grand Total", purchase.GrandTotal, emphasize: true);
        AddAmountRow(stack, "Amount Paid", purchase.AmountPaid);
        AddAmountRow(stack, "Outstanding", purchase.OutstandingAmount, emphasize: purchase.OutstandingAmount > 0);

        return stack;
    }

    private static void AddHeaderCell(Grid grid, string text, int column)
    {
        var cell = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold };
        Grid.SetColumn(cell, column);
        grid.Children.Add(cell);
    }

    private static void AddKeyValue(StackPanel stack, string label, string value) =>
        stack.Children.Add(new TextBlock { Text = $"{label}: {value}", FontSize = 13 });

    private static void AddSeparator(StackPanel stack) =>
        stack.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray), Margin = new Thickness(0, 4, 0, 4) });

    private static void AddAmountRow(StackPanel stack, string label, decimal amount, bool emphasize = false)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelText = new TextBlock { Text = label, FontSize = emphasize ? 15 : 13, FontWeight = emphasize ? FontWeights.Bold : FontWeights.Normal };
        Grid.SetColumn(labelText, 0);
        var valueText = new TextBlock { Text = $"₹{amount:0.00}", FontSize = emphasize ? 15 : 13, FontWeight = emphasize ? FontWeights.Bold : FontWeights.Normal };
        Grid.SetColumn(valueText, 1);

        row.Children.Add(labelText);
        row.Children.Add(valueText);
        stack.Children.Add(row);
    }
}
