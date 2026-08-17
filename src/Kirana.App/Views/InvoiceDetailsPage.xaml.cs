using Kirana.App.Printing;
using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Audit;
using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Printing;
using Kirana.Application.Taxation;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace Kirana.App.Views;

/// <summary>Read-only completed-sale drill-down. It displays the immutable sale snapshots and
/// existing audit records; no edit path is introduced for finalized invoices.</summary>
public sealed partial class InvoiceDetailsPage : Page
{
    private const string Currency = "\u20B9";
    private Sale? _sale;
    private IInvoicePrintService? _invoicePrintService;
    private ManagementSession? _session;

    public InvoiceDetailsPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not int saleId) return;

        var services = App.Services;
        _session = services.GetRequiredService<ManagementSession>();
        _invoicePrintService = services.GetRequiredService<IInvoicePrintService>();
        if (!await services.GetRequiredService<IInvoiceService>().CanAccessAsync(saleId, _session.CurrentUser?.Id))
        {
            ShowError("You do not have access to this invoice.");
            return;
        }

        _sale = await services.GetRequiredService<ISaleService>().GetByIdAsync(saleId);
        if (_sale is null)
        {
            ShowError("Invoice not found.");
            return;
        }

        BuildDetails(_sale);
        await LoadTimelineAsync(services.GetRequiredService<IAuditLogQueryService>());
    }

    private void BuildDetails(Sale sale)
    {
        SummaryGrid.Children.Clear();
        SummaryGrid.ColumnDefinitions.Clear();
        ProductsPanel.Children.Clear();
        TotalsPanel.Children.Clear();
        TotalsPanel.RowDefinitions.Clear();
        PaymentsPanel.Children.Clear();
        GstSummaryPanel.Children.Clear();
        TimelinePanel.Children.Clear();

        SubtitleText.Text = $"{sale.InvoiceNumber} - {sale.SaleDateUtc.ToLocalTime():dd MMM yyyy, hh:mm tt}";
        AddSummary("Invoice Number", sale.InvoiceNumber);
        AddSummary("Customer", sale.Customer?.Name ?? "Walk-in Customer");
        AddSummary("Cashier", sale.CashierUser?.FullName ?? "System");
        AddSummary("Status", sale.Status.ToString());

        // The level this bill was SOLD at, read from the sale itself. Deliberately not derived from
        // today's product prices, which would relabel old invoices whenever a price moved.
        AddSummary("Price Level", sale.PriceLevel.ToDisplayText());
        AddSummary("Customer mobile", sale.Customer?.Phone ?? "-");

        foreach (var item in sale.Items)
        {
            var row = new Grid { Padding = new Thickness(16, 10, 16, 10), ColumnSpacing = 10 };
            foreach (var width in new[] { "*", "72", "82", "86", "115", "105", "82", "90", "96" })
            {
                row.ColumnDefinitions.Add(width == "*"
                    ? new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                    : new ColumnDefinition { Width = new GridLength(double.Parse(width)) });
            }

            AddCell(row, item.ProductNameSnapshot, 0, FontWeights.SemiBold);
            AddCell(row, $"{item.Quantity:0.###} {item.UnitSnapshot}", 1);
            AddCell(row, Money(item.UnitPriceSnapshot), 2, null, HorizontalAlignment.Right);
            AddCell(row, Money(item.DiscountAmount + item.PromotionDiscountAmount), 3, null, HorizontalAlignment.Right);
            var promotionText = string.Join(", ", item.Promotions.Select(p => p.PromotionCodeSnapshot).Distinct());
            AddCell(row, string.IsNullOrWhiteSpace(promotionText) ? "-" : promotionText, 4);
            AddCell(row, item.IsTaxInclusiveSnapshot ? "GST Included" : "GST Added", 5);
            AddCell(row, Money(item.TaxableAmount), 6, null, HorizontalAlignment.Right);
            AddCell(row, $"{item.GstRatePercentSnapshot:0.#}% - {Money(item.GstAmount)}", 7, null, HorizontalAlignment.Right);
            AddCell(row, Money(item.LineTotal), 8, FontWeights.SemiBold, HorizontalAlignment.Right);
            ProductsPanel.Children.Add(row);
        }

        AddTotalRow("Subtotal", sale.SubTotal);
        AddTotalRow("Item discount", sale.ItemDiscountTotal);
        AddTotalRow("Bill discount", sale.BillDiscountAmount);
        AddTotalRow("Promotion savings", sale.PromotionDiscountTotal);
        AddTotalRow("GST", sale.TaxTotal);
        AddTotalRow("Round off", sale.RoundOffAmount);
        AddTotalRow("Grand Total", sale.GrandTotal, true, 10);

        var gstCalculator = App.Services.GetRequiredService<IGstCalculationService>();
        var gstSnapshots = sale.Items.Select(item => new GstSnapshotLine
        {
            TransactionId = sale.Id,
            RatePercent = item.GstRatePercentSnapshot,
            TaxableAmount = item.TaxableAmount,
            GstAmount = item.GstAmount,
            PricingType = item.IsTaxInclusiveSnapshot ? PricingType.Inclusive : PricingType.Exclusive,
        }).ToList();
        gstCalculator.ValidateStored(gstSnapshots, new GstStoredTotals
        {
            TaxableTotal = sale.TaxableTotal,
            GstTotal = sale.TaxTotal,
            RoundOffAmount = sale.RoundOffAmount,
            GrandTotal = sale.GrandTotal,
        }, $"invoice details {sale.InvoiceNumber}");

        foreach (var slab in gstCalculator.SummarizeStored(gstSnapshots))
        {
            var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 7, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var treatment = slab.PricingType == PricingType.Inclusive ? "GST Included" : "GST Added";
            AddCell(row, $"{treatment} ({slab.RatePercent:0.##}%)", 0, FontWeights.SemiBold);
            AddCell(row, Money(slab.TaxableAmount), 1, null, HorizontalAlignment.Right);
            AddCell(row, Money(slab.GstAmount), 2, null, HorizontalAlignment.Right);
            GstSummaryPanel.Children.Add(row);
        }
        TotalGstText.Text = Money(sale.TaxTotal);

        foreach (var payment in sale.Payments)
        {
            var reference = string.IsNullOrWhiteSpace(payment.ReferenceNumber) ? string.Empty : $" - Ref: {payment.ReferenceNumber}";
            var cash = payment.AmountTendered is { } tendered ? $" - Received {Money(tendered)}" : string.Empty;
            var change = payment.ChangeGiven is { } returned && returned > 0 ? $" - Change {Money(returned)}" : string.Empty;
            PaymentsPanel.Children.Add(new TextBlock
            {
                Text = $"{payment.Method}: {Money(payment.Amount)}{reference}{cash}{change}",
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
            });
        }
        if (sale.Payments.Count == 0) PaymentsPanel.Children.Add(new TextBlock { Text = "No payment records available." });
    }

    private async Task LoadTimelineAsync(IAuditLogQueryService auditService)
    {
        if (_sale is null || _session is null) return;
        AddTimeline($"Created - {_sale.SaleDateUtc.ToLocalTime():dd MMM yyyy, hh:mm tt}");
        if (!_session.HasPermission(PermissionKeys.AuditLogView))
        {
            AddTimeline("Audit history is available to users with Audit Log permission.");
            return;
        }

        try
        {
            var entries = await auditService.SearchAsync(new AuditLogQuery { Entity = nameof(Sale), MaxResults = 500 }, _session.CurrentUser?.Id);
            foreach (var entry in entries.Where(entry => entry.EntityId == _sale.Id.ToString()))
            {
                AddTimeline($"{entry.Action} - {entry.TimestampUtc.ToLocalTime():dd MMM yyyy, hh:mm tt}" +
                    (string.IsNullOrWhiteSpace(entry.Reason) ? string.Empty : $" - {entry.Reason}"));
            }
        }
        catch (Exception ex) { AddTimeline($"Could not load audit history: {ex.Message}"); }
    }

    private async void OnPrintReceiptClick(object sender, RoutedEventArgs e) => await ShowPrintAsync(InvoiceFormat.Thermal80mm, false);
    private async void OnPrintGstClick(object sender, RoutedEventArgs e) => await ShowPrintAsync(InvoiceFormat.A4, true);
    private void OnReturnClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SalesReturnsPage));

    private async Task ShowPrintAsync(InvoiceFormat format, bool reprint)
    {
        if (_sale is null || _invoicePrintService is null || _session is null) return;
        if (reprint && !_session.HasPermission(PermissionKeys.SalesReprintInvoice))
        {
            ShowError("You do not have permission to reprint invoices.");
            return;
        }
        var document = await _invoicePrintService.GetInvoiceDocumentAsync(_sale.Id);
        await new InvoicePreviewDialog(new InvoicePreviewViewModel(document, format, _session.CurrentUser?.Id, reprint, _invoicePrintService)).Themed(XamlRoot).ShowAsync();
    }

    private void AddSummary(string label, string value)
    {
        var column = SummaryGrid.ColumnDefinitions.Count;
        SummaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 130 });
        var panel = new StackPanel { Spacing = 3, MinWidth = 0 };
        panel.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 11, Opacity = .72, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(panel, column);
        SummaryGrid.Children.Add(panel);
    }

    private void AddTotalRow(string label, decimal amount, bool isStrong = false, double topMargin = 0)
    {
        var row = TotalsPanel.RowDefinitions.Count;
        TotalsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var labelBlock = new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, topMargin, 0, 0),
            Opacity = isStrong ? 1 : .86,
            FontWeight = isStrong ? FontWeights.SemiBold : FontWeights.Normal,
        };
        var amountBlock = new TextBlock
        {
            Text = Money(amount),
            Margin = new Thickness(0, topMargin, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = isStrong ? FontWeights.SemiBold : FontWeights.Normal,
        };
        // Styled rather than brushed here: pulling a themed brush out of Application.Resources
        // resolves once, so the leader kept its old colour when the light/dark toggle flipped.
        var leader = new Line
        {
            Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["TotalsLeaderLineStyle"],
            Opacity = isStrong ? .75 : .55,
            Margin = new Thickness(4, topMargin + 2, 4, 0),
        };
        Grid.SetRow(labelBlock, row);
        Grid.SetRow(leader, row);
        Grid.SetRow(amountBlock, row);
        Grid.SetColumn(leader, 1);
        Grid.SetColumn(amountBlock, 2);
        TotalsPanel.Children.Add(labelBlock);
        TotalsPanel.Children.Add(leader);
        TotalsPanel.Children.Add(amountBlock);
    }

    private static void AddCell(Grid grid, string text, int column, Windows.UI.Text.FontWeight? weight = null, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var block = new TextBlock
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = alignment,
            TextAlignment = alignment == HorizontalAlignment.Right ? TextAlignment.Right : TextAlignment.Left,
        };
        if (weight is { } value) block.FontWeight = value;
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static string Money(decimal amount) => $"{Currency}{amount:0.00}";
    private void AddTimeline(string text) => TimelinePanel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
    private void ShowError(string message) { ErrorBar.Message = message; ErrorBar.IsOpen = true; }
}
