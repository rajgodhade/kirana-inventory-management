using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Returns;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed class PurchaseReturnRowViewModel
{
    public int Id { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public string PurchaseNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
    public string TotalText { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed partial class PurchaseReturnsViewModel(IPurchaseReturnService returnService, ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanManagePurchases => session.HasPermission(PermissionKeys.PurchasesManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<PurchaseReturnRowViewModel> Returns { get; } = [];

    public async Task InitializeAsync() => await SearchAsync();

    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await returnService.SearchAsync(
                new PurchaseReturnSearchQuery { SearchText = SearchText }, CurrentUserId);

            Returns.Clear();
            foreach (var r in results)
            {
                Returns.Add(new PurchaseReturnRowViewModel
                {
                    Id = r.Id,
                    ReturnNumber = r.ReturnNumber,
                    PurchaseNumber = r.PurchaseNumberSnapshot,
                    SupplierName = r.Supplier.Name,
                    DateText = r.ReturnDateUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"),
                    TotalText = SalesReturnsViewModel.FormatCurrency(r.TotalReturnAmount),
                    ItemCount = r.Items.Count,
                    Reason = r.Reason ?? string.Empty,
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
}

public sealed partial class PurchaseReturnLineViewModel : ObservableObject
{
    public int PurchaseItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal ReceivedQuantity { get; set; }
    public decimal ReturnableQuantity { get; set; }
    public decimal StockOnHand { get; set; }
    public decimal PurchasePrice { get; set; }

    public string ReceivedText => $"{ReceivedQuantity:0.###} {Unit}";
    public string ReturnableText => $"{ReturnableQuantity:0.###} returnable";
    public string StockText => $"{StockOnHand:0.###} in stock";
    public string PriceText => SalesReturnsViewModel.FormatCurrency(PurchasePrice);
    public bool CanReturn => ReturnableQuantity > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotalText))]
    private string _quantityText = "0";

    [ObservableProperty]
    private string _batchNumber = string.Empty;

    public decimal Quantity => decimal.TryParse(QuantityText, out var q) && q > 0 ? q : 0m;

    public string LineTotalText => SalesReturnsViewModel.FormatCurrency(Quantity * PurchasePrice);
}

/// <summary>Backs the New Purchase Return screen.</summary>
public sealed partial class NewPurchaseReturnViewModel(IPurchaseReturnService returnService, ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPurchase))]
    [NotifyPropertyChangedFor(nameof(SelectedPurchaseHeader))]
    private ReturnablePurchase? _selectedPurchase;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _reason = string.Empty;

    public bool CanManagePurchases => session.HasPermission(PermissionKeys.PurchasesManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<ReturnablePurchase> Candidates { get; } = [];

    public ObservableCollection<PurchaseReturnLineViewModel> Lines { get; } = [];

    public bool HasSelectedPurchase => SelectedPurchase is not null;

    public string SelectedPurchaseHeader => SelectedPurchase is null
        ? "No purchase selected"
        : $"{SelectedPurchase.PurchaseNumber} · {SelectedPurchase.PurchaseDateUtc.ToLocalTime():dd-MMM-yyyy} · {SelectedPurchase.SupplierName}";

    public PurchaseReturn? CompletedReturn { get; private set; }

    public async Task SearchAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        IsBusy = true;
        try
        {
            var results = await returnService.FindReturnablePurchasesAsync(SearchText, CurrentUserId);

            Candidates.Clear();
            foreach (var purchase in results)
            {
                Candidates.Add(purchase);
            }

            if (Candidates.Count == 0)
            {
                StatusMessage = "No matching purchase found. Try the purchase number, supplier or a product.";
            }
            else if (Candidates.Count == 1)
            {
                await SelectPurchaseAsync(Candidates[0].PurchaseId);
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

    public async Task SelectPurchaseAsync(int purchaseId)
    {
        ErrorMessage = null;
        try
        {
            SelectedPurchase = await returnService.GetReturnablePurchaseAsync(purchaseId, CurrentUserId);
            Lines.Clear();

            if (SelectedPurchase is null)
            {
                return;
            }

            foreach (var line in SelectedPurchase.Lines)
            {
                Lines.Add(new PurchaseReturnLineViewModel
                {
                    PurchaseItemId = line.PurchaseItemId,
                    ProductName = line.ProductName,
                    ProductCode = line.ProductCode,
                    Unit = line.Unit,
                    ReceivedQuantity = line.ReceivedQuantity,
                    ReturnableQuantity = line.ReturnableQuantity,
                    StockOnHand = line.StockOnHand,
                    PurchasePrice = line.PurchasePrice,
                    BatchNumber = line.BatchNumber ?? string.Empty,
                });
            }

            if (!SelectedPurchase.HasAnythingReturnable)
            {
                StatusMessage = "Everything on this purchase has already been returned.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task<bool> ProcessAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        CompletedReturn = null;

        if (SelectedPurchase is null)
        {
            ErrorMessage = "Select the purchase being returned against first.";
            return false;
        }

        var lines = Lines
            .Where(l => l.Quantity > 0)
            .Select(l => new PurchaseReturnLineInput
            {
                PurchaseItemId = l.PurchaseItemId,
                Quantity = l.Quantity,
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
            CompletedReturn = await returnService.ProcessReturnAsync(new CreatePurchaseReturnRequest
            {
                PurchaseId = SelectedPurchase.PurchaseId,
                Lines = lines,
                Reason = string.IsNullOrWhiteSpace(Reason) ? null : Reason,
                ProcessedByUserId = CurrentUserId,
            });

            StatusMessage = $"{CompletedReturn.ReturnNumber} recorded — {SalesReturnsViewModel.FormatCurrency(CompletedReturn.TotalReturnAmount)} credited to the supplier.";
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
}
