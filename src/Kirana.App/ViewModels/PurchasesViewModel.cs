using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Purchases list/search/filter page (PRD §28).</summary>
public sealed partial class PurchasesViewModel(
    IPurchaseService purchaseService,
    ISupplierService supplierService,
    ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _outstandingOnly;

    [ObservableProperty]
    private Supplier? _selectedSupplierFilter;

    [ObservableProperty]
    private string _selectedStatusFilter = AllStatusesLabel;

    [ObservableProperty]
    private DateTimeOffset? _fromDateFilter;

    [ObservableProperty]
    private DateTimeOffset? _toDateFilter;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public const string AllStatusesLabel = "All statuses";

    public IReadOnlyList<string> StatusFilterOptions { get; } =
        [AllStatusesLabel, "Paid", "Partially Paid", "Outstanding"];

    /// <summary>Suppliers for the filter dropdown — the same directory <c>SupplierPickerDialog</c>
    /// already loads, just via <see cref="ISupplierService.SearchAsync"/> with no search text (i.e.
    /// everything). Loaded once; suppliers don't change often enough to warrant refreshing per
    /// search.</summary>
    public ObservableCollection<Supplier> SupplierFilterOptions { get; } = [];

    public bool CanManagePurchases => session.HasPermission(PermissionKeys.PurchasesManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<PurchaseRowViewModel> Purchases { get; } = [];

    [ObservableProperty]
    private bool _hasResults;

    // --- Summary cards. Computed from whatever SearchAsync currently has loaded (same 200-row cap
    // the list itself has always had), NOT from a separate all-time query — there is no existing
    // service method for that, and adding one is out of scope for a UI-only pass. The cards
    // therefore summarize "what's on screen", same as the CustomersPage outstanding strip
    // summarizes its own loaded set. ---

    [ObservableProperty]
    private decimal _totalPurchasesAmount;

    [ObservableProperty]
    private decimal _totalOutstandingAmount;

    [ObservableProperty]
    private decimal _totalPaidAmount;

    [ObservableProperty]
    private decimal _thisMonthPurchasesAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThisMonthPurchasesCountText))]
    private int _thisMonthPurchasesCount;

    public string ThisMonthPurchasesCountText => ThisMonthPurchasesCount == 1
        ? "1 purchase"
        : $"{ThisMonthPurchasesCount} purchases";

    public async Task InitializeAsync()
    {
        await LoadSupplierFilterOptionsAsync();
        await SearchAsync();
    }

    /// <summary>Sentinel "All Suppliers" entry — a client-side-only <see cref="Supplier"/> instance
    /// (never persisted, Id 0 can never collide with a real supplier) so the filter ComboBox always
    /// has a selectable "no filter" state instead of fighting WinUI's lack of a built-in way to
    /// deselect back to null.</summary>
    private static readonly Supplier AllSuppliersOption = new() { Id = 0, Name = "All Suppliers" };

    private async Task LoadSupplierFilterOptionsAsync()
    {
        SupplierFilterOptions.Clear();
        SupplierFilterOptions.Add(AllSuppliersOption);
        SelectedSupplierFilter = AllSuppliersOption;

        try
        {
            var suppliers = await supplierService.SearchAsync(new SupplierSearchQuery(), CurrentUserId);
            foreach (var supplier in suppliers.OrderBy(s => s.Name))
            {
                SupplierFilterOptions.Add(supplier);
            }
        }
        catch
        {
            // The supplier filter is a convenience, not core to the page — leave it at "All
            // Suppliers" rather than blocking the purchase list itself from loading.
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await purchaseService.SearchAsync(
                new PurchaseSearchQuery
                {
                    SearchText = SearchText,
                    OutstandingOnly = OutstandingOnly,
                    SupplierId = SelectedSupplierFilter is { Id: > 0 } supplier ? supplier.Id : null,
                    FromUtc = FromDateFilter?.UtcDateTime,
                    // Inclusive end-of-day: a picked "To" date should still catch a purchase made
                    // any time that day, not just at midnight.
                    ToUtc = ToDateFilter?.UtcDateTime.Date.AddDays(1).AddTicks(-1),
                },
                CurrentUserId);

            var rows = results.Select(ToRow);

            // Payment status has no equivalent field on PurchaseSearchQuery — it's derived, not
            // stored — so it's applied here rather than by changing the query/service.
            if (SelectedStatusFilter != AllStatusesLabel)
            {
                rows = SelectedStatusFilter switch
                {
                    "Paid" => rows.Where(r => r.IsPaid),
                    "Partially Paid" => rows.Where(r => r.IsPartiallyPaid),
                    "Outstanding" => rows.Where(r => r.IsFullyOutstanding),
                    _ => rows,
                };
            }

            Purchases.Clear();
            var index = 0;
            foreach (var row in rows)
            {
                row.RowIndex = index++;
                Purchases.Add(row);
            }

            HasResults = Purchases.Count > 0;
            RecalculateSummary();
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

    private void RecalculateSummary()
    {
        TotalPurchasesAmount = Purchases.Sum(p => p.GrandTotal);
        TotalOutstandingAmount = Purchases.Sum(p => p.OutstandingAmount);
        TotalPaidAmount = Purchases.Sum(p => p.AmountPaid);

        var now = DateTime.Now;
        var thisMonth = Purchases.Where(p =>
        {
            var local = p.PurchaseDateUtc.ToLocalTime();
            return local.Year == now.Year && local.Month == now.Month;
        }).ToList();

        ThisMonthPurchasesAmount = thisMonth.Sum(p => p.GrandTotal);
        ThisMonthPurchasesCount = thisMonth.Count;
    }

    public void ClearFilters()
    {
        SearchText = string.Empty;
        OutstandingOnly = false;
        SelectedSupplierFilter = AllSuppliersOption;
        SelectedStatusFilter = AllStatusesLabel;
        FromDateFilter = null;
        ToDateFilter = null;
    }

    public void ClearDateFilters()
    {
        FromDateFilter = null;
        ToDateFilter = null;
    }

    public Task<Purchase?> GetPurchaseAsync(int purchaseId) => purchaseService.GetByIdAsync(purchaseId, CurrentUserId);

    public async Task<bool> RecordPaymentAsync(int purchaseId, int supplierId, decimal amount, PaymentMethod method, string? referenceNumber)
    {
        ErrorMessage = null;
        try
        {
            await purchaseService.RecordPaymentAsync(new RecordSupplierPaymentRequest
            {
                SupplierId = supplierId,
                PurchaseId = purchaseId,
                Amount = amount,
                Method = method,
                ReferenceNumber = referenceNumber,
                RecordedByUserId = CurrentUserId,
            });
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    private static PurchaseRowViewModel ToRow(Purchase purchase) => new()
    {
        Id = purchase.Id,
        SupplierId = purchase.SupplierId,
        PurchaseNumber = purchase.PurchaseNumber,
        SupplierName = purchase.Supplier.Name,
        DateText = purchase.PurchaseDateUtc.ToLocalTime().ToString("dd-MMM-yyyy"),
        PurchaseDateUtc = purchase.PurchaseDateUtc,
        GrandTotal = purchase.GrandTotal,
        AmountPaid = purchase.AmountPaid,
        OutstandingAmount = purchase.OutstandingAmount,
    };
}
