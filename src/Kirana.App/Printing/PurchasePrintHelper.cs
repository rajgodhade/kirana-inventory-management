using Kirana.Application.Printing;
using Kirana.Domain.Entities;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;

namespace Kirana.App.Printing;

/// <summary>
/// Prints a finalized purchase through the native Windows print pipeline, for the shopkeeper's own
/// records. Mirrors <see cref="CustomerReceiptPrintHelper"/> exactly: single-use, single page, no
/// pagination. Purely presentational — never touches the underlying <see cref="Purchase"/>, so a
/// failed, offline or cancelled print is always safe to retry.
/// </summary>
public sealed class PurchasePrintHelper : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly PrintManager _printManager;
    private readonly PrintDocument _printDocument;
    private readonly Purchase _purchase;
    private readonly double _widthDip;
    private UIElement? _page;

    public IPrintDocumentSource PrintDocumentSource { get; }

    public PurchasePrintHelper(Window window, Purchase purchase)
    {
        _purchase = purchase;
        _widthDip = InvoiceLayoutCalculator.MillimetersToDips(InvoiceLayoutCalculator.GetPageWidthMillimeters(InvoiceFormat.A4));

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
            $"Purchase {_purchase.PurchaseNumber}",
            sourceArgs => sourceArgs.SetSource(PrintDocumentSource));

    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _page = PurchasePrintElementRenderer.BuildElement(_purchase, _widthDip);
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
