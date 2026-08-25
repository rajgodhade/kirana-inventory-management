using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>One row in the Sales Returns list.</summary>
public sealed class SalesReturnRowViewModel
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
    public string CustomerName { get; set; } = "Walk-in";
    public string TotalText { get; set; } = string.Empty;
    public string RefundText { get; set; } = string.Empty;
    public string RefundMethod { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}

/// <summary>Backs the Sales Returns list screen.</summary>
public sealed partial class SalesReturnsViewModel(ISalesReturnService returnService, ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanProcessReturns => session.HasPermission(PermissionKeys.SalesProcessRefund);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<SalesReturnRowViewModel> Returns { get; } = [];

    public async Task InitializeAsync() => await SearchAsync();

    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await returnService.SearchAsync(new SalesReturnSearchQuery { SearchText = SearchText }, CurrentUserId);

            Returns.Clear();
            foreach (var r in results)
            {
                Returns.Add(new SalesReturnRowViewModel
                {
                    Id = r.Id,
                    ReturnNumber = r.ReturnNumber,
                    InvoiceNumber = r.InvoiceNumberSnapshot,
                    DateText = r.ReturnDateUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"),
                    CustomerName = r.Sale.GstIdentitySnapshotCapturedAtUtc is not null
                        ? r.Sale.CustomerNameSnapshot ?? "Walk-in"
                        : r.Customer?.Name ?? "Walk-in",
                    TotalText = FormatCurrency(r.TotalReturnAmount),
                    RefundText = FormatCurrency(r.RefundAmount),
                    RefundMethod = r.RefundMethod == RefundMethod.None ? "No refund" : r.RefundMethod.ToString(),
                    ItemCount = r.Items.Count,
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal static string FormatCurrency(decimal amount) =>
        "₹" + amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
}

/// <summary>One selectable line on the New Return screen.</summary>
public sealed partial class ReturnLineViewModel : ObservableObject
{
    public int SaleItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public bool TracksBatches { get; set; }
    public decimal SoldQuantity { get; set; }
    public decimal ReturnableQuantity { get; set; }
    public decimal UnitPrice { get; set; }

    public string SoldText => $"{SoldQuantity:0.###} {Unit}";
    public string ReturnableText => $"{ReturnableQuantity:0.###} returnable";
    public string UnitPriceText => SalesReturnsViewModel.FormatCurrency(UnitPrice);
    public bool CanReturn => ReturnableQuantity > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotalText))]
    private string _quantityText = "0";

    [ObservableProperty]
    private bool _isDamaged;

    [ObservableProperty]
    private string _batchNumber = string.Empty;

    public decimal Quantity => decimal.TryParse(QuantityText, out var q) && q > 0 ? q : 0m;

    public string LineTotalText => SalesReturnsViewModel.FormatCurrency(Quantity * UnitPrice);
}

/// <summary>
/// Backs the New Sales Return screen: find the sale, pick lines and quantities, choose disposition
/// and refund method. All validation is re-run by the service — this only shapes the request.
/// </summary>
public sealed partial class NewSalesReturnViewModel(ISalesReturnService returnService, ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSale))]
    [NotifyPropertyChangedFor(nameof(SelectedSaleHeader))]
    private ReturnableSale? _selectedSale;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _refundReference = string.Empty;

    [ObservableProperty]
    private string _reason = string.Empty;

    public IReadOnlyList<string> RefundMethods { get; } = ["Cash", "UPI", "Card", "Store Credit", "No refund"];

    [ObservableProperty]
    private string _selectedRefundMethod = "Cash";

    public bool CanProcessReturns => session.HasPermission(PermissionKeys.SalesProcessRefund);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<ReturnableSale> Candidates { get; } = [];

    public ObservableCollection<ReturnLineViewModel> Lines { get; } = [];

    public bool HasSelectedSale => SelectedSale is not null;

    public string SelectedSaleHeader => SelectedSale is null
        ? "No invoice selected"
        : $"{SelectedSale.InvoiceNumber} · {SelectedSale.SaleDateUtc.ToLocalTime():dd-MMM-yyyy} · {SelectedSale.CustomerName ?? "Walk-in"}";

    /// <summary>The return just created, so the page can offer to print its receipt.</summary>
    public SalesReturn? CompletedReturn { get; private set; }

    public async Task SearchAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        try
        {
            var results = await returnService.FindReturnableSalesAsync(
                new SaleLookupQuery { SearchText = SearchText }, CurrentUserId);

            Candidates.Clear();
            foreach (var sale in results)
            {
                Candidates.Add(sale);
            }

            if (Candidates.Count == 0)
            {
                StatusMessage = "No matching sale found. Try the invoice number, a barcode, a product or the customer.";
            }
            else if (Candidates.Count == 1)
            {
                // A single hit is almost always what the user wanted — open it straight away.
                await SelectSaleAsync(Candidates[0].SaleId);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SelectSaleAsync(int saleId)
    {
        ErrorMessage = null;
        try
        {
            SelectedSale = await returnService.GetReturnableSaleAsync(saleId, CurrentUserId);
            Lines.Clear();

            if (SelectedSale is null)
            {
                return;
            }

            foreach (var line in SelectedSale.Lines)
            {
                Lines.Add(new ReturnLineViewModel
                {
                    SaleItemId = line.SaleItemId,
                    ProductName = line.ProductName,
                    ProductCode = line.ProductCode,
                    Unit = line.Unit,
                    TracksBatches = line.TracksBatches,
                    SoldQuantity = line.SoldQuantity,
                    ReturnableQuantity = line.ReturnableQuantity,
                    UnitPrice = line.UnitPrice,
                });
            }

            if (!SelectedSale.HasAnythingReturnable)
            {
                StatusMessage = "Everything on this invoice has already been returned.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Processes the return. Returns true on success, with <see cref="CompletedReturn"/>
    /// set; otherwise <see cref="ErrorMessage"/> explains why.</summary>
    public async Task<bool> ProcessAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        CompletedReturn = null;

        if (SelectedSale is null)
        {
            ErrorMessage = "Select the invoice being returned against first.";
            return false;
        }

        var lines = Lines
            .Where(l => l.Quantity > 0)
            .Select(l => new SalesReturnLineInput
            {
                SaleItemId = l.SaleItemId,
                Quantity = l.Quantity,
                Disposition = l.IsDamaged ? ReturnDisposition.Damaged : ReturnDisposition.ReturnToStock,
                BatchNumber = string.IsNullOrWhiteSpace(l.BatchNumber) ? null : l.BatchNumber,
            })
            .ToList();

        if (lines.Count == 0)
        {
            ErrorMessage = "Enter a return quantity on at least one line.";
            return false;
        }

        IsBusy = true;
        try
        {
            CompletedReturn = await returnService.ProcessReturnAsync(new CreateSalesReturnRequest
            {
                SaleId = SelectedSale.SaleId,
                Lines = lines,
                RefundMethod = ParseRefundMethod(SelectedRefundMethod),
                ReferenceNumber = string.IsNullOrWhiteSpace(RefundReference) ? null : RefundReference,
                Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason,
                ProcessedByUserId = CurrentUserId,
            });

            StatusMessage = $"{CompletedReturn.ReturnNumber} recorded — {SalesReturnsViewModel.FormatCurrency(CompletedReturn.RefundAmount)} refunded.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static RefundMethod ParseRefundMethod(string display) => display switch
    {
        "UPI" => RefundMethod.Upi,
        "Card" => RefundMethod.Card,
        "Store Credit" => RefundMethod.StoreCredit,
        "No refund" => RefundMethod.None,
        _ => RefundMethod.Cash,
    };
}
