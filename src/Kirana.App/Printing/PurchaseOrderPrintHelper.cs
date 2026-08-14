using Kirana.Application.Printing;
using Kirana.Domain.Entities;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;

namespace Kirana.App.Printing;

public sealed class PurchaseOrderPrintHelper : IDisposable
{
    private readonly PrintManager _manager;
    private readonly PrintDocument _document = new();
    private readonly PurchaseOrder _order;
    private UIElement? _page;
    public PurchaseOrderPrintHelper(Window window, PurchaseOrder order)
    {
        _order = order;
        _manager = PrintManagerInterop.GetForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        _manager.PrintTaskRequested += OnTaskRequested;
        _document.Paginate += OnPaginate; _document.GetPreviewPage += OnPreview; _document.AddPages += OnAddPages;
    }
    public async Task ShowAsync()
    {
        if (!PrintManager.IsSupported()) throw new InvalidOperationException("Printing is not supported on this device.");
        await PrintManagerInterop.ShowPrintUIForWindowAsync(WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
    }
    private void OnTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args) => args.Request.CreatePrintTask($"Purchase Order {_order.PurchaseOrderNumber}", e => e.SetSource(_document.DocumentSource));
    private void OnPaginate(object sender, PaginateEventArgs e) { _page = BuildPage(); _document.SetPreviewPageCount(1, PreviewPageCountType.Final); }
    private void OnPreview(object sender, GetPreviewPageEventArgs e) => _document.SetPreviewPage(e.PageNumber, _page);
    private void OnAddPages(object sender, AddPagesEventArgs e) { if (_page is not null) _document.AddPage(_page); _document.AddPagesComplete(); }
    private FrameworkElement BuildPage()
    {
        var stack = new StackPanel { Width = InvoiceLayoutCalculator.MillimetersToDips(210), Padding = new Thickness(32), Spacing = 7 };
        stack.Children.Add(new TextBlock { Text = "PURCHASE ORDER", FontSize = 22, FontWeight = FontWeights.Bold });
        Add(stack, "PO Number", _order.PurchaseOrderNumber); Add(stack, "Supplier", $"{_order.SupplierNameSnapshot} ({_order.SupplierCodeSnapshot})");
        Add(stack, "Date", _order.OrderDateUtc.ToLocalTime().ToString("dd MMM yyyy")); Add(stack, "Status", _order.Status.ToString());
        stack.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 6, 0, 6) });
        foreach (var item in _order.Items) Add(stack, item.ProductNameSnapshot, $"{item.OrderedQuantity:0.###} {item.UnitSnapshot} × ₹{item.UnitCost:0.00}     ₹{item.LineTotal:0.00}");
        stack.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 6, 0, 6) });
        Add(stack, "Subtotal", $"₹{_order.SubTotal:0.00}"); Add(stack, "Discount", $"₹{_order.DiscountTotal:0.00}"); Add(stack, "GST", $"₹{_order.TaxTotal:0.00}");
        stack.Children.Add(new TextBlock { Text = $"Expected Total: ₹{_order.GrandTotal:0.00}", FontSize = 18, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
        stack.Children.Add(new TextBlock { Text = "This purchase order is not proof of goods receipt or payment.", Margin = new Thickness(0, 12, 0, 0) });
        return stack;
    }
    private static void Add(Panel panel, string label, string value) => panel.Children.Add(new TextBlock { Text = $"{label}: {value}", TextWrapping = TextWrapping.Wrap });
    public void Dispose() { _manager.PrintTaskRequested -= OnTaskRequested; _document.Paginate -= OnPaginate; _document.GetPreviewPage -= OnPreview; _document.AddPages -= OnAddPages; }
}
