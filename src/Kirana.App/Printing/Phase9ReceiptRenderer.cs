using Kirana.Application.Printing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Printing;

/// <summary>
/// Renders the Phase 9 slips — sales-return/refund receipts and expense vouchers — as WinUI
/// elements. Thermal width only and single page, exactly like
/// <see cref="CustomerReceiptElementRenderer"/>: these are counter slips, not invoices, so there is
/// no A4 variant and no pagination. Colours are left to the default brushes so the print pipeline
/// renders them onto the page without the app's theme background.
/// </summary>
public static class Phase9ReceiptRenderer
{
    public static FrameworkElement BuildReturnReceipt(ReturnReceiptDocument document, double widthDip)
    {
        var stack = NewSheet(widthDip);

        AddStoreHeader(stack, document.StoreName, document.StoreAddress, document.StoreContactNumber,
            document.IsRefund ? "REFUND RECEIPT" : "RETURN NOTE");

        AddSeparator(stack);
        AddKeyValue(stack, "Return", document.ReturnNumber);
        AddKeyValue(stack, "Against invoice", document.InvoiceNumber);
        AddKeyValue(stack, "Date", document.ReturnDateUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"));

        if (!string.IsNullOrWhiteSpace(document.ProcessedByName))
        {
            AddKeyValue(stack, "Processed by", document.ProcessedByName!);
        }

        if (!string.IsNullOrWhiteSpace(document.CustomerName))
        {
            AddSeparator(stack);
            AddKeyValue(stack, "Customer", document.CustomerName!);
            if (!string.IsNullOrWhiteSpace(document.CustomerCode))
            {
                AddKeyValue(stack, "Customer ID", document.CustomerCode!);
            }
        }

        AddSeparator(stack);
        stack.Children.Add(new TextBlock
        {
            Text = "RETURNED ITEMS",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
        });

        foreach (var line in document.Lines)
        {
            AddReturnLine(stack, line);
        }

        AddSeparator(stack);
        AddAmountRow(stack, "Value of goods", document.TotalReturnAmount);
        AddAmountRow(stack, document.IsRefund ? "Refunded" : "Refund", document.RefundAmount, emphasize: true);
        AddKeyValue(stack, "Method", document.IsRefund ? document.RefundMethod : "No refund (exchange/adjustment)");

        if (!string.IsNullOrWhiteSpace(document.ReferenceNumber))
        {
            AddKeyValue(stack, "Reference", document.ReferenceNumber!);
        }

        if (!string.IsNullOrWhiteSpace(document.Reason))
        {
            AddSeparator(stack);
            AddWrapped(stack, $"Reason: {document.Reason}");
        }

        AddFooter(stack, document.FooterText);
        return stack;
    }

    public static FrameworkElement BuildExpenseReceipt(ExpenseReceiptDocument document, double widthDip)
    {
        var stack = NewSheet(widthDip);

        AddStoreHeader(stack, document.StoreName, document.StoreAddress, document.StoreContactNumber, "EXPENSE VOUCHER");

        AddSeparator(stack);
        AddKeyValue(stack, "Voucher", document.ExpenseNumber);
        AddKeyValue(stack, "Date", document.ExpenseDateUtc.ToLocalTime().ToString("dd-MMM-yyyy"));
        AddKeyValue(stack, "Category", document.CategoryName);
        AddKeyValue(stack, "Mode", document.PaymentMethod);

        if (!string.IsNullOrWhiteSpace(document.ReferenceNumber))
        {
            AddKeyValue(stack, "Reference", document.ReferenceNumber!);
        }

        if (!string.IsNullOrWhiteSpace(document.RecordedByName))
        {
            AddKeyValue(stack, "Recorded by", document.RecordedByName!);
        }

        AddSeparator(stack);
        AddAmountRow(stack, "Amount paid", document.Amount, emphasize: true);

        if (!string.IsNullOrWhiteSpace(document.Description))
        {
            AddSeparator(stack);
            AddWrapped(stack, document.Description!);
        }

        if (!string.IsNullOrWhiteSpace(document.Notes))
        {
            AddWrapped(stack, document.Notes!, opacity: 0.8);
        }

        AddFooter(stack, document.FooterText);
        return stack;
    }

    // ------------------------------------------------------------------ pieces

    private static StackPanel NewSheet(double widthDip) => new()
    {
        Width = widthDip,
        Padding = new Thickness(8),
        Spacing = 4,
    };

    private static void AddStoreHeader(StackPanel stack, string storeName, string? address, string? phone, string title)
    {
        stack.Children.Add(new TextBlock
        {
            Text = storeName,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(address))
        {
            stack.Children.Add(new TextBlock
            {
                Text = address,
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            stack.Children.Add(new TextBlock { Text = $"Ph: {phone}", FontSize = 10, TextAlignment = TextAlignment.Center });
        }

        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
    }

    private static void AddReturnLine(StackPanel stack, ReturnReceiptLine line)
    {
        var grid = TwoColumnGrid();

        var left = new TextBlock { Text = line.ProductName, FontSize = 10, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(left, 0);

        var right = new TextBlock
        {
            Text = $"₹{line.LineRefundAmount:0.00}",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(right, 1);

        grid.Children.Add(left);
        grid.Children.Add(right);
        stack.Children.Add(grid);

        stack.Children.Add(new TextBlock
        {
            Text = $"   {line.Quantity:0.###} {line.Unit} × ₹{line.UnitPrice:0.00}   ·   {line.Disposition}",
            FontSize = 9,
            Opacity = 0.75,
        });
    }

    private static void AddAmountRow(StackPanel stack, string label, decimal amount, bool emphasize = false)
    {
        var grid = TwoColumnGrid();
        var weight = emphasize ? FontWeights.Bold : FontWeights.Normal;
        var size = emphasize ? 12 : 10;

        var labelBlock = new TextBlock { Text = label, FontSize = size, FontWeight = weight };
        Grid.SetColumn(labelBlock, 0);

        var amountBlock = new TextBlock
        {
            Text = $"₹{amount:0.00}",
            FontSize = size,
            FontWeight = weight,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(amountBlock, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(amountBlock);
        stack.Children.Add(grid);
    }

    private static void AddKeyValue(StackPanel stack, string key, string value)
    {
        var grid = TwoColumnGrid();

        var keyBlock = new TextBlock { Text = key + ":", FontSize = 10, Opacity = 0.8 };
        Grid.SetColumn(keyBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(valueBlock, 1);

        grid.Children.Add(keyBlock);
        grid.Children.Add(valueBlock);
        stack.Children.Add(grid);
    }

    private static void AddWrapped(StackPanel stack, string text, double opacity = 1.0) =>
        stack.Children.Add(new TextBlock { Text = text, FontSize = 9, Opacity = opacity, TextWrapping = TextWrapping.Wrap });

    private static void AddSeparator(StackPanel stack) =>
        stack.Children.Add(new TextBlock
        {
            Text = new string('-', 42),
            FontSize = 9,
            Opacity = 0.6,
            TextAlignment = TextAlignment.Center,
        });

    private static void AddFooter(StackPanel stack, string? footerText)
    {
        AddSeparator(stack);
        if (!string.IsNullOrWhiteSpace(footerText))
        {
            stack.Children.Add(new TextBlock
            {
                Text = footerText,
                FontSize = 9,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private static Grid TwoColumnGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }
}
