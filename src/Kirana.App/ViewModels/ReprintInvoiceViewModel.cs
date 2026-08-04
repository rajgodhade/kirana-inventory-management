using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Printing;

namespace Kirana.App.ViewModels;

/// <summary>Backs the "Reprint Invoice" search screen (PRD §23) — looks up a completed sale by
/// invoice number. Finding/viewing a sale here has no side effects; the caller is responsible for
/// requiring manager authorization (<see cref="Domain.Entities.PermissionKeys.SalesReprintInvoice"/>)
/// before actually opening the invoice preview/print dialog for the result.</summary>
public sealed partial class ReprintInvoiceViewModel(IInvoicePrintService invoicePrintService) : ObservableObject
{
    [ObservableProperty]
    private string _invoiceNumberText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string? _resultSummary;

    public InvoiceDocument? FoundDocument { get; private set; }

    public async Task<bool> SearchAsync()
    {
        ErrorMessage = null;
        FoundDocument = null;
        HasResult = false;
        ResultSummary = null;

        var invoiceNumber = InvoiceNumberText.Trim();
        if (invoiceNumber.Length == 0)
        {
            ErrorMessage = "Enter an invoice number.";
            return false;
        }

        IsSearching = true;
        try
        {
            FoundDocument = await invoicePrintService.GetInvoiceDocumentByInvoiceNumberAsync(invoiceNumber);
            HasResult = true;
            ResultSummary =
                $"{FoundDocument.InvoiceNumber} — {FoundDocument.SaleDateUtc.ToLocalTime():dd-MMM-yyyy hh:mm tt}\n" +
                $"Customer: {FoundDocument.CustomerName ?? "Walk-in"}\n" +
                $"Grand Total: ₹{FoundDocument.GrandTotal:0.00}";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsSearching = false;
        }
    }
}
