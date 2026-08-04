using Kirana.Application.Printing;
using Kirana.Application.Reports;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;
using Windows.UI.Text;

namespace Kirana.App.Printing;

/// <summary>
/// Prints a <see cref="ReportExportData"/> as a paginated A4 table through the native Windows print
/// pipeline — this is Kirana's "Export to PDF": there is no PDF library in this project, so the
/// user picks "Microsoft Print to PDF" (built into Windows) from the same system print dialog every
/// other document in this app already uses (PRD "reuse the existing printing/document
/// infrastructure", "do not redesign the printing system"). Mirrors <see cref="InvoicePrintHelper"/>'s
/// pagination approach: <see cref="InvoiceLayoutCalculator.Chunk{T}"/> splits rows across pages
/// once the printer's actual page height is known.
/// </summary>
public sealed class ReportPrintHelper : IDisposable
{
    private const double ReservedHeightDip = 140;
    private const double RowHeightDip = 26;

    private readonly IntPtr _hwnd;
    private readonly PrintManager _printManager;
    private readonly PrintDocument _printDocument;
    private readonly ReportExportData _data;
    private readonly double _widthDip;
    private readonly List<UIElement> _pages = [];

    public IPrintDocumentSource PrintDocumentSource { get; }

    public ReportPrintHelper(Window window, ReportExportData data)
    {
        _data = data;
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
        args.Request.CreatePrintTask($"Kirana Report — {_data.Title}", sourceArgs => sourceArgs.SetSource(PrintDocumentSource));

    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _pages.Clear();

        var options = (PrintTaskOptions)e.PrintTaskOptions;
        var pageHeight = options.GetPageDescription(0).PageSize.Height;
        var rowsPerPage = Math.Max(1, (int)Math.Floor((pageHeight - ReservedHeightDip) / RowHeightDip));

        var rowPages = InvoiceLayoutCalculator.Chunk(_data.Rows, rowsPerPage);
        if (rowPages.Count == 0)
        {
            rowPages = [Array.Empty<IReadOnlyList<string>>()];
        }

        for (var i = 0; i < rowPages.Count; i++)
        {
            _pages.Add(BuildPage(rowPages[i], isFirstPage: i == 0, pageNumber: i + 1, totalPages: rowPages.Count));
        }

        _printDocument.SetPreviewPageCount(_pages.Count, PreviewPageCountType.Final);
    }

    private void OnGetPreviewPage(object sender, GetPreviewPageEventArgs e) =>
        _printDocument.SetPreviewPage(e.PageNumber, _pages[e.PageNumber - 1]);

    private void OnAddPages(object sender, AddPagesEventArgs e)
    {
        foreach (var page in _pages)
        {
            _printDocument.AddPage(page);
        }

        _printDocument.AddPagesComplete();
    }

    private FrameworkElement BuildPage(IReadOnlyList<IReadOnlyList<string>> rows, bool isFirstPage, int pageNumber, int totalPages)
    {
        var stack = new StackPanel { Width = _widthDip, Padding = new Thickness(32), Spacing = 6 };

        if (isFirstPage)
        {
            stack.Children.Add(new TextBlock { Text = _data.Title, FontSize = 20, FontWeight = FontWeights.Bold });
            if (!string.IsNullOrWhiteSpace(_data.Subtitle))
            {
                stack.Children.Add(new TextBlock { Text = _data.Subtitle, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 8) });
            }
        }

        stack.Children.Add(BuildRow(_data.Columns, FontWeights.SemiBold, isHeader: true));

        foreach (var row in rows)
        {
            stack.Children.Add(BuildRow(row, FontWeights.Normal, isHeader: false));
        }

        if (totalPages > 1)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"Page {pageNumber} of {totalPages}",
                FontSize = 9,
                Opacity = 0.6,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }

        return stack;
    }

    private FrameworkElement BuildRow(IReadOnlyList<string> cells, FontWeight weight, bool isHeader)
    {
        var grid = new Grid();
        for (var i = 0; i < cells.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cell = new TextBlock
            {
                Text = cells[i],
                FontSize = 10,
                FontWeight = weight,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }

        if (isHeader)
        {
            return new Border
            {
                Child = grid,
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 4),
                Margin = new Thickness(0, 0, 0, 4),
            };
        }

        return grid;
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
