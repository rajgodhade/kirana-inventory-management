using Kirana.Application.Printing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Printing;

/// <summary>
/// Renders an Udhaar repayment receipt (PRD §31) as WinUI elements for preview and printing.
/// Deliberately thermal-width only: a repayment receipt is a short counter slip, not an invoice, so
/// unlike <see cref="InvoiceElementRenderer"/> there is no A4 variant and no pagination — the
/// allocation list is bounded by how many open credits one payment can settle.
/// </summary>
public static class CustomerReceiptElementRenderer
{
    public static FrameworkElement BuildReceiptElement(CustomerReceiptDocument document, double widthDip)
    {
        // Colors are left to the default brushes, exactly as InvoiceElementRenderer does — the print
        // pipeline renders these onto the page without the app's theme background.
        var stack = new StackPanel
        {
            Width = widthDip,
            Padding = new Thickness(8),
            Spacing = 4,
        };

        AddHeader(stack, document);
        AddSeparator(stack);
        AddKeyValue(stack, "Receipt", document.ReceiptNumber);
        AddKeyValue(stack, "Date", document.PaymentDateUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"));

        if (!string.IsNullOrWhiteSpace(document.ReceivedByName))
        {
            AddKeyValue(stack, "Received by", document.ReceivedByName!);
        }

        AddSeparator(stack);
        AddKeyValue(stack, "Customer", document.CustomerName);
        AddKeyValue(stack, "Customer ID", document.CustomerCode);

        if (!string.IsNullOrWhiteSpace(document.CustomerPhone))
        {
            AddKeyValue(stack, "Phone", document.CustomerPhone!);
        }

        AddSeparator(stack);

        stack.Children.Add(new TextBlock
        {
            Text = "UDHAAR REPAYMENT",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
        });

        AddAmountRow(stack, "Amount Received", document.AmountPaid, emphasize: true);
        AddKeyValue(stack, "Mode", document.PaymentMethod);

        if (!string.IsNullOrWhiteSpace(document.ReferenceNumber))
        {
            AddKeyValue(stack, "Reference", document.ReferenceNumber!);
        }

        AddSeparator(stack);
        AddAmountRow(stack, "Previous Balance", document.BalanceBefore);
        AddAmountRow(stack, "Balance Due", document.BalanceAfter, emphasize: true);

        if (document.Allocations.Count > 0)
        {
            AddSeparator(stack);
            stack.Children.Add(new TextBlock
            {
                Text = "Applied to:",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
            });

            foreach (var allocation in document.Allocations)
            {
                AddAllocationLine(stack, allocation);
            }
        }

        if (!string.IsNullOrWhiteSpace(document.Notes))
        {
            AddSeparator(stack);
            stack.Children.Add(new TextBlock
            {
                Text = document.Notes,
                FontSize = 9,
                Opacity = 0.8,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        AddSeparator(stack);

        if (!string.IsNullOrWhiteSpace(document.FooterText))
        {
            stack.Children.Add(new TextBlock
            {
                Text = document.FooterText,
                FontSize = 9,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return stack;
    }

    private static void AddHeader(StackPanel stack, CustomerReceiptDocument document)
    {
        stack.Children.Add(new TextBlock
        {
            Text = document.StoreName,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(document.StoreAddress))
        {
            stack.Children.Add(new TextBlock
            {
                Text = document.StoreAddress,
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        if (!string.IsNullOrWhiteSpace(document.StoreContactNumber))
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"Ph: {document.StoreContactNumber}",
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = "PAYMENT RECEIPT",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
    }

    private static void AddAllocationLine(StackPanel stack, CustomerReceiptAllocationLine allocation)
    {
        var grid = TwoColumnGrid();

        var left = new TextBlock
        {
            Text = $"{allocation.InvoiceNumber} ({allocation.SaleDateUtc.ToLocalTime():dd-MMM-yy})",
            FontSize = 9,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(left, 0);

        var right = new TextBlock
        {
            Text = $"₹{allocation.AmountApplied:0.00}",
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(right, 1);

        grid.Children.Add(left);
        grid.Children.Add(right);
        stack.Children.Add(grid);
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

    private static void AddSeparator(StackPanel stack) =>
        stack.Children.Add(new TextBlock
        {
            Text = new string('-', 42),
            FontSize = 9,
            Opacity = 0.6,
            TextAlignment = TextAlignment.Center,
        });

    private static Grid TwoColumnGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }
}
