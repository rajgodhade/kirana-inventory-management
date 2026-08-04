using Kirana.Application.Printing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;

namespace Kirana.App.Printing;

/// <summary>
/// Wires a built <see cref="InvoiceDocument"/> into the native Windows print pipeline (PRD §23),
/// reusing the exact pattern proven in <see cref="BarcodeLabelPrintHelper"/>: register with
/// <see cref="PrintManager"/> via <see cref="PrintManagerInterop"/> (required for unpackaged
/// WinUI 3 desktop apps), paginate content by however tall the user's chosen printer reports its
/// page, and hand pages to <see cref="PrintDocument"/>. One instance is single-use: create it
/// right before printing and <see cref="Dispose"/> it after. A print failure, offline printer, or
/// a cancelled system print dialog only ever affects this helper — the underlying
/// <c>Sale</c>/<c>Payment</c>/stock records were already committed before this type exists, so
/// none of that is at risk here; callers can safely retry by constructing a new instance.
/// </summary>
public sealed class InvoicePrintHelper : IDisposable
{
    private const double CompactReservedHeightDip = 260;
    private const double CompactLineHeightDip = 34;
    private const double A4ReservedHeightDip = 360;
    private const double A4LineHeightDip = 26;

    private readonly IntPtr _hwnd;
    private readonly PrintManager _printManager;
    private readonly PrintDocument _printDocument;
    private readonly InvoiceDocument _document;
    private readonly InvoiceFormat _format;
    private readonly double _widthDip;
    private readonly List<UIElement> _printPages = [];

    public IPrintDocumentSource PrintDocumentSource { get; }

    public InvoicePrintHelper(Window window, InvoiceDocument document, InvoiceFormat format)
    {
        _document = document;
        _format = format;
        _widthDip = InvoiceLayoutCalculator.MillimetersToDips(InvoiceLayoutCalculator.GetPageWidthMillimeters(format));

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _printManager = PrintManagerInterop.GetForWindow(_hwnd);
        _printManager.PrintTaskRequested += OnPrintTaskRequested;

        _printDocument = new PrintDocument();
        PrintDocumentSource = _printDocument.DocumentSource;
        _printDocument.Paginate += OnPaginate;
        _printDocument.GetPreviewPage += OnGetPreviewPage;
        _printDocument.AddPages += OnAddPages;
    }

    /// <summary>Shows the native Windows print dialog; the user picks a printer there — this is
    /// how printer discovery/selection happens for the actual print job. Throws if printing isn't
    /// supported, or completes normally (with no side effects beyond the print spool) if the user
    /// cancels the dialog — the caller decides what "cancelled" means for its own UI.</summary>
    public async Task ShowPrintUIAsync()
    {
        if (!PrintManager.IsSupported())
        {
            throw new InvalidOperationException("Printing is not supported on this device.");
        }

        await PrintManagerInterop.ShowPrintUIForWindowAsync(_hwnd);
    }

    private void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        args.Request.CreatePrintTask($"Kirana Invoice {_document.InvoiceNumber}", sourceArgs => sourceArgs.SetSource(PrintDocumentSource));
    }

    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _printPages.Clear();

        var options = (PrintTaskOptions)e.PrintTaskOptions;
        var pageSize = options.GetPageDescription(0).PageSize;

        var isCompact = _format != InvoiceFormat.A4;
        var reservedHeight = isCompact ? CompactReservedHeightDip : A4ReservedHeightDip;
        var lineHeight = isCompact ? CompactLineHeightDip : A4LineHeightDip;
        var linesPerPage = Math.Max(1, (int)Math.Floor((pageSize.Height - reservedHeight) / lineHeight));

        var linePages = InvoiceLayoutCalculator.Chunk(_document.Lines, linesPerPage);

        for (var i = 0; i < linePages.Count; i++)
        {
            var page = InvoiceElementRenderer.BuildPageElement(
                _document, _format, linePages[i],
                isFirstPage: i == 0, isLastPage: i == linePages.Count - 1,
                _widthDip, pageSize.Height);

            _printPages.Add(page);
        }

        _printDocument.SetPreviewPageCount(_printPages.Count, PreviewPageCountType.Intermediate);
    }

    private void OnGetPreviewPage(object sender, GetPreviewPageEventArgs e) =>
        _printDocument.SetPreviewPage(e.PageNumber, _printPages[e.PageNumber - 1]);

    private void OnAddPages(object sender, AddPagesEventArgs e)
    {
        foreach (var page in _printPages)
        {
            _printDocument.AddPage(page);
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
