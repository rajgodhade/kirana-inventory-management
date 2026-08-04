using Kirana.Application.Printing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;

namespace Kirana.App.Printing;

/// <summary>
/// Shared plumbing for the Phase 9 single-page thermal slips, following
/// <see cref="CustomerReceiptPrintHelper"/> exactly. Single-use: construct, print, dispose. A
/// failed, offline or cancelled print never touches the underlying record — it was committed
/// before this type exists — so retrying is always safe.
/// </summary>
public abstract class SinglePageReceiptPrintHelper : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly PrintManager _printManager;
    private readonly PrintDocument _printDocument;
    private readonly string _jobName;
    private UIElement? _page;

    protected double WidthDip { get; }

    public IPrintDocumentSource PrintDocumentSource { get; }

    protected SinglePageReceiptPrintHelper(Window window, InvoiceFormat format, string jobName)
    {
        _jobName = jobName;

        // A4 makes no sense for a counter slip; fall back to the 80mm roll.
        WidthDip = InvoiceLayoutCalculator.MillimetersToDips(
            InvoiceLayoutCalculator.GetPageWidthMillimeters(
                format == InvoiceFormat.A4 ? InvoiceFormat.Thermal80mm : format));

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _printManager = PrintManagerInterop.GetForWindow(_hwnd);
        _printManager.PrintTaskRequested += OnPrintTaskRequested;

        _printDocument = new PrintDocument();
        PrintDocumentSource = _printDocument.DocumentSource;
        _printDocument.Paginate += OnPaginate;
        _printDocument.GetPreviewPage += OnGetPreviewPage;
        _printDocument.AddPages += OnAddPages;
    }

    /// <summary>Builds the one page this slip consists of.</summary>
    protected abstract FrameworkElement BuildPage();

    public async Task ShowPrintUIAsync()
    {
        if (!PrintManager.IsSupported())
        {
            throw new InvalidOperationException("Printing is not supported on this device.");
        }

        await PrintManagerInterop.ShowPrintUIForWindowAsync(_hwnd);
    }

    private void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args) =>
        args.Request.CreatePrintTask(_jobName, sourceArgs => sourceArgs.SetSource(PrintDocumentSource));

    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _page = BuildPage();
        _printDocument.SetPreviewPageCount(1, PreviewPageCountType.Final);
    }

    private void OnGetPreviewPage(object sender, GetPreviewPageEventArgs e) =>
        _printDocument.SetPreviewPage(e.PageNumber, _page);

    private void OnAddPages(object sender, AddPagesEventArgs e)
    {
        if (_page is not null)
        {
            _printDocument.AddPage(_page);
        }

        _printDocument.AddPagesComplete();
    }

    public void Dispose()
    {
        _printDocument.Paginate -= OnPaginate;
        _printDocument.GetPreviewPage -= OnGetPreviewPage;
        _printDocument.AddPages -= OnAddPages;
        _printManager.PrintTaskRequested -= OnPrintTaskRequested;
        GC.SuppressFinalize(this);
    }
}

public sealed class ReturnReceiptPrintHelper(Window window, ReturnReceiptDocument document, InvoiceFormat format)
    : SinglePageReceiptPrintHelper(window, format, $"Kirana Return {document.ReturnNumber}")
{
    protected override FrameworkElement BuildPage() => Phase9ReceiptRenderer.BuildReturnReceipt(document, WidthDip);
}

public sealed class ExpenseReceiptPrintHelper(Window window, ExpenseReceiptDocument document, InvoiceFormat format)
    : SinglePageReceiptPrintHelper(window, format, $"Kirana Expense {document.ExpenseNumber}")
{
    protected override FrameworkElement BuildPage() => Phase9ReceiptRenderer.BuildExpenseReceipt(document, WidthDip);
}
