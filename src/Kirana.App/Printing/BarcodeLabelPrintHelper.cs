using Kirana.Application.Barcodes;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Printing;
using Windows.Foundation;
using Windows.Graphics.Printing;

namespace Kirana.App.Printing;

/// <summary>
/// Wires a set of pre-built <see cref="LabelPrintItem"/>s into the native Windows print pipeline
/// (PRD §16, §23's printing pattern applied to labels): register with <see cref="PrintManager"/>
/// via <see cref="PrintManagerInterop"/> (required for unpackaged WinUI 3 desktop apps — there is
/// no CoreWindow to call the UWP-style <c>GetForCurrentView</c>), paginate labels into a grid
/// sized to whatever page the user's printer reports, and hand pages to <see cref="PrintDocument"/>.
/// One instance is single-use: create it right before printing and <see cref="Dispose"/> it after.
/// </summary>
public sealed class BarcodeLabelPrintHelper : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly PrintManager _printManager;
    private readonly PrintDocument _printDocument;
    private readonly IReadOnlyList<LabelPrintItem> _items;
    private readonly double _labelWidthDip;
    private readonly double _labelHeightDip;
    private readonly List<UIElement> _printPages = [];

    public IPrintDocumentSource PrintDocumentSource { get; }

    public BarcodeLabelPrintHelper(Window window, IReadOnlyList<LabelPrintItem> items, double labelWidthMm, double labelHeightMm)
    {
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one label is required.", nameof(items));
        }

        _items = items;
        _labelWidthDip = LabelLayoutCalculator.MillimetersToDips(labelWidthMm);
        _labelHeightDip = LabelLayoutCalculator.MillimetersToDips(labelHeightMm);

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _printManager = PrintManagerInterop.GetForWindow(_hwnd);
        _printManager.PrintTaskRequested += OnPrintTaskRequested;

        _printDocument = new PrintDocument();
        PrintDocumentSource = _printDocument.DocumentSource;
        _printDocument.Paginate += OnPaginate;
        _printDocument.GetPreviewPage += OnGetPreviewPage;
        _printDocument.AddPages += OnAddPages;
    }

    /// <summary>Shows the native Windows print dialog; the user picks a printer/settings there.</summary>
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
        args.Request.CreatePrintTask("Kirana Barcode Labels", sourceArgs => sourceArgs.SetSource(PrintDocumentSource));
    }

    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _printPages.Clear();

        var options = (PrintTaskOptions)e.PrintTaskOptions;
        var pageDescription = options.GetPageDescription(0);

        var (columns, rows) = LabelLayoutCalculator.CalculateGrid(
            pageDescription.PageSize.Width, pageDescription.PageSize.Height, _labelWidthDip, _labelHeightDip);
        var perPage = Math.Max(1, columns * rows);

        foreach (var pageItems in LabelLayoutCalculator.Chunk(_items, perPage))
        {
            _printPages.Add(BuildPage(pageItems, columns, rows, pageDescription.PageSize));
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

    private UIElement BuildPage(IReadOnlyList<LabelPrintItem> pageItems, int columns, int rows, Size pageSize)
    {
        var grid = new Grid { Width = pageSize.Width, Height = pageSize.Height };

        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_labelWidthDip) });
        }

        for (var r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_labelHeightDip) });
        }

        for (var i = 0; i < pageItems.Count; i++)
        {
            var label = BuildLabelElement(pageItems[i], _labelWidthDip, _labelHeightDip);
            Grid.SetColumn(label, i % columns);
            Grid.SetRow(label, i / columns);
            grid.Children.Add(label);
        }

        return grid;
    }

    /// <summary>Builds one label's visual — shared by print pages and the in-dialog preview list
    /// so what you see is exactly what prints.</summary>
    public static FrameworkElement BuildLabelElement(LabelPrintItem item, double width, double height)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        stack.Children.Add(new TextBlock
        {
            Text = item.ProductName,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        });

        stack.Children.Add(new Image
        {
            Source = item.BarcodeImage,
            Width = Math.Max(20, width - 8),
            Height = Math.Max(20, height * 0.45),
            Stretch = Stretch.Fill,
        });

        stack.Children.Add(new TextBlock { Text = item.BarcodeValue, FontSize = 8, TextAlignment = TextAlignment.Center });
        stack.Children.Add(new TextBlock { Text = item.CodeOrSku, FontSize = 7, Opacity = 0.8, TextAlignment = TextAlignment.Center });
        stack.Children.Add(new TextBlock
        {
            Text = $"MRP ₹{item.Mrp:0.##}   ₹{item.SellingPrice:0.##}",
            FontSize = 8,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
        });

        return new Border
        {
            Width = width,
            Height = height,
            BorderBrush = new SolidColorBrush(Colors.Black),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(4),
            Child = stack,
        };
    }

    public void Dispose()
    {
        _printDocument.Paginate -= OnPaginate;
        _printDocument.GetPreviewPage -= OnGetPreviewPage;
        _printDocument.AddPages -= OnAddPages;
        _printManager.PrintTaskRequested -= OnPrintTaskRequested;
    }
}
