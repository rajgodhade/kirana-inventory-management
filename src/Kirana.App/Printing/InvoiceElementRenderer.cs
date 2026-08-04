using Kirana.Application.Printing;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kirana.App.Printing;

/// <summary>
/// Builds the WinUI visual tree for an <see cref="InvoiceDocument"/> — shared by the in-app
/// preview and the native print pipeline so what the cashier sees is exactly what prints, the
/// same principle <see cref="BarcodeLabelPrintHelper"/> uses for labels. 58mm/80mm render as a
/// compact two-line-per-item receipt; A4 renders as a full tabular tax invoice with an HSN/GST%
/// column, per PRD §23.
/// </summary>
public static class InvoiceElementRenderer
{
    /// <summary>The full, unpaginated document — used for the on-screen preview inside a
    /// ScrollViewer, where physical page breaks don't matter.</summary>
    public static FrameworkElement BuildFullDocumentElement(InvoiceDocument document, InvoiceFormat format, double widthDip)
    {
        var isCompact = format != InvoiceFormat.A4;
        var stack = new StackPanel { Width = widthDip, Padding = new Thickness(isCompact ? 8 : 24), Spacing = isCompact ? 4 : 8 };

        stack.Children.Add(BuildHeader(document, isCompact));
        stack.Children.Add(BuildDivider());
        stack.Children.Add(BuildLineItemsSection(document.Lines, isCompact));
        stack.Children.Add(BuildDivider());
        stack.Children.Add(BuildTotalsSection(document, isCompact));
        stack.Children.Add(BuildDivider());
        stack.Children.Add(BuildPaymentsSection(document.Payments, isCompact));

        if (!string.IsNullOrWhiteSpace(document.FooterText))
        {
            stack.Children.Add(BuildDivider());
            stack.Children.Add(new TextBlock
            {
                Text = document.FooterText,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = isCompact ? 10 : 12,
                Opacity = 0.85,
            });
        }

        return stack;
    }

    /// <summary>One printable page: the header/customer block only on the first page, the
    /// totals/payments/footer only on the last page, and just this page's chunk of line items in
    /// between — so a long cart can be split across multiple physical pages/roll-feeds.</summary>
    public static FrameworkElement BuildPageElement(
        InvoiceDocument document, InvoiceFormat format, IReadOnlyList<InvoiceLine> pageLines,
        bool isFirstPage, bool isLastPage, double widthDip, double heightDip)
    {
        var isCompact = format != InvoiceFormat.A4;
        var stack = new StackPanel
        {
            Width = widthDip,
            MinHeight = heightDip,
            Padding = new Thickness(isCompact ? 8 : 24),
            Spacing = isCompact ? 4 : 8,
        };

        if (isFirstPage)
        {
            stack.Children.Add(BuildHeader(document, isCompact));
            stack.Children.Add(BuildDivider());
        }

        stack.Children.Add(BuildLineItemsSection(pageLines, isCompact));

        if (isLastPage)
        {
            stack.Children.Add(BuildDivider());
            stack.Children.Add(BuildTotalsSection(document, isCompact));
            stack.Children.Add(BuildDivider());
            stack.Children.Add(BuildPaymentsSection(document.Payments, isCompact));

            if (!string.IsNullOrWhiteSpace(document.FooterText))
            {
                stack.Children.Add(BuildDivider());
                stack.Children.Add(new TextBlock
                {
                    Text = document.FooterText,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = isCompact ? 10 : 12,
                    Opacity = 0.85,
                });
            }
        }
        else
        {
            stack.Children.Add(new TextBlock { Text = "(continued…)", FontSize = 10, Opacity = 0.6, TextAlignment = TextAlignment.Center });
        }

        return stack;
    }

    private static FrameworkElement BuildHeader(InvoiceDocument document, bool isCompact)
    {
        var panel = new StackPanel { Spacing = 2 };

        panel.Children.Add(new TextBlock
        {
            Text = document.StoreName,
            FontSize = isCompact ? 16 : 22,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(document.StoreAddress))
        {
            panel.Children.Add(new TextBlock { Text = document.StoreAddress, FontSize = isCompact ? 10 : 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        }

        var contactLine = string.Join("   ", new[]
        {
            document.StoreContactNumber is { } phone ? $"Ph: {phone}" : null,
            document.StoreGstin is { } gstin ? $"GSTIN: {gstin}" : null,
        }.Where(s => s is not null));

        if (contactLine.Length > 0)
        {
            panel.Children.Add(new TextBlock { Text = contactLine, FontSize = isCompact ? 10 : 12, TextAlignment = TextAlignment.Center });
        }

        panel.Children.Add(new TextBlock
        {
            Text = "TAX INVOICE",
            FontSize = isCompact ? 11 : 14,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        });

        panel.Children.Add(BuildKeyValueRow("Invoice #", document.InvoiceNumber, isCompact));
        panel.Children.Add(BuildKeyValueRow("Date", document.SaleDateUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"), isCompact));

        if (!string.IsNullOrWhiteSpace(document.CashierName))
        {
            panel.Children.Add(BuildKeyValueRow("Cashier", document.CashierName, isCompact));
        }

        if (!string.IsNullOrWhiteSpace(document.CustomerName))
        {
            panel.Children.Add(BuildKeyValueRow("Customer", document.CustomerName, isCompact));

            if (!string.IsNullOrWhiteSpace(document.CustomerPhone))
            {
                panel.Children.Add(BuildKeyValueRow("Phone", document.CustomerPhone, isCompact));
            }

            if (!string.IsNullOrWhiteSpace(document.CustomerGstin))
            {
                panel.Children.Add(BuildKeyValueRow("Customer GSTIN", document.CustomerGstin, isCompact));
            }
        }

        return panel;
    }

    private static FrameworkElement BuildLineItemsSection(IReadOnlyList<InvoiceLine> lines, bool isCompact)
    {
        var panel = new StackPanel { Spacing = isCompact ? 4 : 2 };

        if (isCompact)
        {
            foreach (var line in lines)
            {
                panel.Children.Add(BuildCompactLine(line));
            }
        }
        else
        {
            panel.Children.Add(BuildA4HeaderRow());
            foreach (var line in lines)
            {
                panel.Children.Add(BuildA4Row(line));
            }
        }

        return panel;
    }

    private static FrameworkElement BuildCompactLine(InvoiceLine line)
    {
        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock { Text = line.ProductName, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(name, 0);

        var total = new TextBlock { Text = $"₹{line.LineTotal:0.00}", FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(total, 1);

        grid.Children.Add(name);
        grid.Children.Add(total);

        var detailParts = new List<string> { $"{line.Quantity:0.###} {line.Unit} x ₹{line.UnitPrice:0.##}" };
        if (line.DiscountPercent > 0)
        {
            detailParts.Add($"Disc {line.DiscountPercent:0.##}%");
        }

        if (line.GstRatePercent > 0)
        {
            detailParts.Add($"GST {line.GstRatePercent:0.##}%");
        }

        var stack = new StackPanel { Spacing = 1 };
        stack.Children.Add(grid);
        stack.Children.Add(new TextBlock { Text = string.Join("   ", detailParts), FontSize = 9, Opacity = 0.75 });

        return stack;
    }

    private static readonly (string Header, double Width)[] A4Columns =
    [
        ("Item", 0), ("HSN", 90), ("Qty", 60), ("Rate", 80), ("Disc%", 60), ("GST%", 60), ("Amount", 90),
    ];

    private static FrameworkElement BuildA4HeaderRow()
    {
        var grid = BuildA4Grid();
        for (var i = 0; i < A4Columns.Length; i++)
        {
            var cell = new TextBlock { Text = A4Columns[i].Header, FontWeight = FontWeights.SemiBold, FontSize = 12 };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private static FrameworkElement BuildA4Row(InvoiceLine line)
    {
        var grid = BuildA4Grid();
        var values = new[]
        {
            line.ProductName,
            line.HsnCode ?? "-",
            $"{line.Quantity:0.###} {line.Unit}",
            $"₹{line.UnitPrice:0.##}",
            line.DiscountPercent > 0 ? $"{line.DiscountPercent:0.##}%" : "-",
            line.GstRatePercent > 0 ? $"{line.GstRatePercent:0.##}%" : "-",
            $"₹{line.LineTotal:0.00}",
        };

        for (var i = 0; i < values.Length; i++)
        {
            var cell = new TextBlock { Text = values[i], FontSize = 11, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        return grid;
    }

    private static Grid BuildA4Grid()
    {
        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var (_, width) in A4Columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        }

        return grid;
    }

    private static FrameworkElement BuildTotalsSection(InvoiceDocument document, bool isCompact)
    {
        var panel = new StackPanel { Spacing = 1 };

        panel.Children.Add(BuildAmountRow("Subtotal", document.SubTotal, isCompact));

        if (document.ItemDiscountTotal > 0)
        {
            panel.Children.Add(BuildAmountRow("Item Discount", -document.ItemDiscountTotal, isCompact));
        }

        if (document.BillDiscountAmount > 0)
        {
            panel.Children.Add(BuildAmountRow($"Bill Discount ({document.BillDiscountPercent:0.##}%)", -document.BillDiscountAmount, isCompact));
        }

        foreach (var group in document.GstGroups)
        {
            panel.Children.Add(BuildAmountRow($"GST @ {group.RatePercent:0.##}% (on ₹{group.TaxableAmount:0.00})", group.GstAmount, isCompact));
        }

        if (document.RoundOffAmount != 0)
        {
            panel.Children.Add(BuildAmountRow("Round Off", document.RoundOffAmount, isCompact));
        }

        panel.Children.Add(BuildDivider());
        panel.Children.Add(BuildAmountRow("Grand Total", document.GrandTotal, isCompact, emphasize: true));

        return panel;
    }

    private static FrameworkElement BuildPaymentsSection(IReadOnlyList<InvoicePaymentLine> payments, bool isCompact)
    {
        var panel = new StackPanel { Spacing = 1 };

        foreach (var payment in payments)
        {
            var label = payment.Method.ToString();
            if (!string.IsNullOrWhiteSpace(payment.ReferenceNumber))
            {
                label += $" (Ref: {payment.ReferenceNumber})";
            }

            panel.Children.Add(BuildAmountRow(label, payment.Amount, isCompact));

            if (payment.AmountTendered is { } tendered)
            {
                panel.Children.Add(BuildAmountRow("  Cash Received", tendered, isCompact));
                panel.Children.Add(BuildAmountRow("  Change Returned", payment.ChangeGiven ?? 0, isCompact));
            }
        }

        return panel;
    }

    private static FrameworkElement BuildAmountRow(string label, decimal amount, bool isCompact, bool emphasize = false)
    {
        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fontSize = emphasize ? (isCompact ? 14 : 18) : (isCompact ? 11 : 13);
        var weight = emphasize ? FontWeights.Bold : FontWeights.Normal;

        var labelBlock = new TextBlock { Text = label, FontSize = fontSize, FontWeight = weight };
        Grid.SetColumn(labelBlock, 0);

        var amountBlock = new TextBlock { Text = $"₹{amount:0.00}", FontSize = fontSize, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(amountBlock, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(amountBlock);
        return grid;
    }

    private static FrameworkElement BuildKeyValueRow(string key, string value, bool isCompact)
    {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyBlock = new TextBlock { Text = key + ":", FontSize = isCompact ? 10 : 12, Opacity = 0.8 };
        Grid.SetColumn(keyBlock, 0);

        var valueBlock = new TextBlock { Text = value, FontSize = isCompact ? 10 : 12, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(valueBlock, 1);

        grid.Children.Add(keyBlock);
        grid.Children.Add(valueBlock);
        return grid;
    }

    private static FrameworkElement BuildDivider() => new Border
    {
        BorderBrush = new SolidColorBrush(Colors.Gray),
        BorderThickness = new Thickness(0, 0.5, 0, 0),
        Margin = new Thickness(0, 2, 0, 2),
    };
}
