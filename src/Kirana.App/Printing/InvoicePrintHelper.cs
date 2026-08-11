using Kirana.Application.Printing;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    private readonly TaskCompletionSource<bool> _printCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PrintTask? _activePrintTask;

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

        // Showing the Windows print UI only confirms that the dialog opened; preview pagination
        // and printing continue asynchronously afterward. Keep this helper (and its event
        // handlers) alive until Windows reports completion/cancellation, otherwise the preview
        // remains permanently on "Loading preview".
        await _printCompletion.Task.WaitAsync(TimeSpan.FromMinutes(30));
    }

    private void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        _activePrintTask = args.Request.CreatePrintTask(
            $"Kirana Invoice {_document.InvoiceNumber}",
            sourceArgs => sourceArgs.SetSource(PrintDocumentSource));
        if (_format == InvoiceFormat.A4)
        {
            _activePrintTask.Options.MediaSize = PrintMediaSize.IsoA4;
            _activePrintTask.Options.Orientation = PrintOrientation.Portrait;
        }
        _activePrintTask.Completed += OnPrintTaskCompleted;
    }

    private void OnPrintTaskCompleted(PrintTask sender, PrintTaskCompletedEventArgs args) =>
        _printCompletion.TrySetResult(true);

    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _printPages.Clear();

        var options = (PrintTaskOptions)e.PrintTaskOptions;
        var pageDescription = options.GetPageDescription(0);
        var pageSize = pageDescription.PageSize;
        var imageableRect = pageDescription.ImageableRect;

        var isCompact = _format != InvoiceFormat.A4;
        var reservedHeight = isCompact ? CompactReservedHeightDip : A4ReservedHeightDip;
        var lineHeight = isCompact ? CompactLineHeightDip : A4LineHeightDip;
        var availableHeight = imageableRect.Height > 0 ? imageableRect.Height : pageSize.Height;
        var availableWidth = imageableRect.Width > 0 ? imageableRect.Width : pageSize.Width;
        var contentWidth = Math.Min(_widthDip, availableWidth);
        var linesPerPage = Math.Max(1, (int)Math.Floor((availableHeight - reservedHeight) / lineHeight));

        IReadOnlyList<IReadOnlyList<InvoiceLine>> linePages = InvoiceLayoutCalculator.Chunk(_document.Lines, linesPerPage);
        if (linePages.Count == 0)
            linePages = [Array.Empty<InvoiceLine>()];

        for (var i = 0; i < linePages.Count; i++)
        {
            var content = InvoiceElementRenderer.BuildPageElement(
                _document, _format, linePages[i],
                isFirstPage: i == 0, isLastPage: i == linePages.Count - 1,
                contentWidth, availableHeight);

            // Always hand Windows a full physical page. The selected invoice format controls the
            // content width: A4 fills the imageable area, while 58/80mm receipts remain their real
            // roll width and are centered when previewed through an A4-only PDF driver.
            content.HorizontalAlignment = HorizontalAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Top;
            content.Margin = new Thickness(0, Math.Max(0, imageableRect.Y), 0, 0);
            var page = new Grid
            {
                Width = pageSize.Width,
                Height = pageSize.Height,
                Background = new SolidColorBrush(Colors.White),
            };
            page.Children.Add(content);

            _printPages.Add(page);
        }

        _printDocument.SetPreviewPageCount(_printPages.Count, PreviewPageCountType.Final);
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
        if (_activePrintTask is not null)
            _activePrintTask.Completed -= OnPrintTaskCompleted;
        _printDocument.Paginate -= OnPaginate;
        _printDocument.GetPreviewPage -= OnGetPreviewPage;
        _printDocument.AddPages -= OnAddPages;
        _printManager.PrintTaskRequested -= OnPrintTaskRequested;
    }
}
