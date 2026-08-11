using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.App.Printing;
using Kirana.Application.Printing;
using Microsoft.UI.Xaml;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs the invoice preview/print dialog (PRD §23) — shown both right after completing a sale
/// and from the separate "Reprint Invoice" flow. Printing (or a cancelled/failed print) never
/// touches the underlying <see cref="Domain.Entities.Sale"/> — it was already committed before
/// this ViewModel exists — so a failed print can always be retried by clicking Print again.
/// </summary>
public sealed partial class InvoicePreviewViewModel : ObservableObject
{
    private readonly IInvoicePrintService _invoicePrintService;
    private readonly int? _userId;
    private readonly bool _isReprint;

    public InvoiceDocument Document { get; }

    public IReadOnlyList<InvoiceFormat> AvailableFormats { get; } = Enum.GetValues<InvoiceFormat>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DialogContentWidth))]
    [NotifyPropertyChangedFor(nameof(PreviewViewportWidth))]
    [NotifyPropertyChangedFor(nameof(PreviewViewportMaxHeight))]
    private InvoiceFormat _selectedFormat;

    [ObservableProperty]
    private FrameworkElement? _previewElement;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isPrinting;

    public InvoicePreviewViewModel(
        InvoiceDocument document, InvoiceFormat initialFormat, int? userId, bool isReprint,
        IInvoicePrintService invoicePrintService)
    {
        Document = document;
        _userId = userId;
        _isReprint = isReprint;
        _invoicePrintService = invoicePrintService;

        _selectedFormat = initialFormat;
        RebuildPreview();
    }

    public double DialogContentWidth => SelectedFormat == InvoiceFormat.A4 ? 980 : 460;
    public double PreviewViewportWidth => SelectedFormat == InvoiceFormat.A4 ? 940 : 430;
    public double PreviewViewportMaxHeight => SelectedFormat == InvoiceFormat.A4 ? 640 : 520;
    public string CustomerSummary => string.IsNullOrWhiteSpace(Document.CustomerName) ? "Walk-in Customer" : Document.CustomerName;

    partial void OnSelectedFormatChanged(InvoiceFormat value) => RebuildPreview();

    private void RebuildPreview()
    {
        var widthDip = InvoiceLayoutCalculator.MillimetersToDips(
            InvoiceLayoutCalculator.GetPageWidthMillimeters(SelectedFormat));
        PreviewElement = InvoiceElementRenderer.BuildFullDocumentElement(Document, SelectedFormat, widthDip);
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        IsPrinting = true;
        try
        {
            using var printHelper = new InvoicePrintHelper(App.MainWindow, Document, SelectedFormat);
            await printHelper.ShowPrintUIAsync();

            await _invoicePrintService.LogPrintAsync(Document.SaleId, _userId, _isReprint);
            StatusMessage = "Print dialog completed. If nothing printed, the printer may be offline — you can retry.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Printing failed: {ex.Message}. The sale is already saved — retry printing any time.";
        }
        finally
        {
            IsPrinting = false;
        }
    }
}
