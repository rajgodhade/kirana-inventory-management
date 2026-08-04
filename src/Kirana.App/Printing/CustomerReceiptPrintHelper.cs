using Kirana.Application.Printing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;

namespace Kirana.App.Printing;

/// <summary>
/// Prints an Udhaar repayment receipt through the native Windows print pipeline, reusing the
/// <see cref="InvoicePrintHelper"/> pattern. Simpler than the invoice helper by design: a receipt is
/// always a single thermal-width page, so there is no pagination step. Single-use — construct,
/// print, dispose. A failed, offline or cancelled print never touches the underlying
/// <c>CreditPayment</c>, which was committed before this type exists, so retrying is always safe.
/// </summary>
public sealed class CustomerReceiptPrintHelper : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly PrintManager _printManager;
    private readonly PrintDocument _printDocument;
    private readonly CustomerReceiptDocument _document;
    private readonly double _widthDip;
    private UIElement? _page;

    public IPrintDocumentSource PrintDocumentSource { get; }

    public CustomerReceiptPrintHelper(Window window, CustomerReceiptDocument document, InvoiceFormat format)
    {
        _document = document;
        _widthDip = InvoiceLayoutCalculator.MillimetersToDips(
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

    public async Task ShowPrintUIAsync()
    {
        if (!PrintManager.IsSupported())
        {
            throw new InvalidOperationException("Printing is not supported on this device.");
        }

        await PrintManagerInterop.ShowPrintUIForWindowAsync(_hwnd);
    }

    private void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args) =>
        args.Request.CreatePrintTask(
            $"Kirana Receipt {_document.ReceiptNumber}",
            sourceArgs => sourceArgs.SetSource(PrintDocumentSource));

    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _page = CustomerReceiptElementRenderer.BuildReceiptElement(_document, _widthDip);
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
    }
}
