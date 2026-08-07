using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Suppliers management page (PRD §28-29).</summary>
public sealed partial class SuppliersViewModel(ISupplierService supplierService, ManagementSession session) : ObservableObject
{
    private readonly List<SupplierRowViewModel> _allSuppliers = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _outstandingOnly;

    [ObservableProperty]
    private string _selectedStatusFilter = "All suppliers";

    [ObservableProperty]
    private string _selectedSortOption = "Name";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanManagePurchases => session.HasPermission(PermissionKeys.PurchasesManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<SupplierRowViewModel> Suppliers { get; } = [];
    public IReadOnlyList<string> StatusFilterOptions { get; } = ["All suppliers", "Active", "Inactive", "Overdue"];
    public IReadOnlyList<string> SortOptions { get; } = ["Name", "Outstanding", "Last purchase"];

    [ObservableProperty] private string _totalSuppliersText = "0";
    [ObservableProperty] private string _activeSuppliersText = "0";
    [ObservableProperty] private decimal _outstandingPayable;
    [ObservableProperty] private string _overdueSuppliersText = "0";

    public bool HasResults => Suppliers.Count > 0;

    public async Task InitializeAsync() => await SearchAsync();

    [RelayCommand]
    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await supplierService.SearchOverviewAsync(
                new SupplierSearchQuery
                {
                    SearchText = SearchText,
                    IncludeInactive = true,
                    MaxResults = 1000,
                },
                CurrentUserId);

            _allSuppliers.Clear();
            _allSuppliers.AddRange(results.Select(ToRow));
            UpdateSummary();

            IEnumerable<SupplierRowViewModel> filtered = _allSuppliers;
            filtered = SelectedStatusFilter switch
            {
                "Active" => filtered.Where(x => x.IsActive),
                "Inactive" => filtered.Where(x => !x.IsActive),
                "Overdue" => filtered.Where(x => x.IsOverdue),
                _ => filtered,
            };
            if (OutstandingOnly)
            {
                filtered = filtered.Where(x => x.HasOutstanding);
            }

            filtered = SelectedSortOption switch
            {
                "Outstanding" => filtered.OrderByDescending(x => x.OutstandingBalance).ThenBy(x => x.Name),
                "Last purchase" => filtered.OrderByDescending(x => x.LastPurchaseDateUtc).ThenBy(x => x.Name),
                _ => filtered.OrderBy(x => x.Name),
            };

            Suppliers.Clear();
            foreach (var supplier in filtered)
            {
                Suppliers.Add(supplier);
            }
            OnPropertyChanged(nameof(HasResults));
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

    public Task<Supplier> CreateSupplierAsync(CreateSupplierRequest request) => supplierService.CreateAsync(request);

    public Task<Supplier> UpdateSupplierAsync(int supplierId, UpdateSupplierRequest request) => supplierService.UpdateAsync(supplierId, request);

    public async Task SetActiveAsync(int supplierId, bool isActive) =>
        await supplierService.SetActiveAsync(supplierId, isActive, CurrentUserId);

    public Task<Supplier?> GetSupplierAsync(int supplierId) => supplierService.GetByIdAsync(supplierId, CurrentUserId);

    public async Task ShowOutstandingAsync()
    {
        OutstandingOnly = true;
        SelectedStatusFilter = "All suppliers";
        await SearchAsync();
    }

    public async Task ShowActiveAsync()
    {
        OutstandingOnly = false;
        SelectedStatusFilter = "Active";
        await SearchAsync();
    }

    public async Task ShowOverdueAsync()
    {
        OutstandingOnly = false;
        SelectedStatusFilter = "Overdue";
        await SearchAsync();
    }

    private void UpdateSummary()
    {
        TotalSuppliersText = _allSuppliers.Count.ToString("N0");
        ActiveSuppliersText = _allSuppliers.Count(x => x.IsActive).ToString("N0");
        OutstandingPayable = _allSuppliers.Where(x => x.IsActive).Sum(x => x.OutstandingBalance);
        OverdueSuppliersText = _allSuppliers.Count(x => x.IsActive && x.IsOverdue).ToString("N0");
    }

    private static SupplierRowViewModel ToRow(SupplierOverview supplier) => new()
    {
        Id = supplier.Id,
        SupplierCode = supplier.SupplierCode,
        Name = supplier.Name,
        ContactPerson = supplier.ContactPerson,
        Phone = supplier.Phone,
        OutstandingBalance = supplier.OutstandingBalance,
        IsActive = supplier.IsActive,
        LastPurchaseDateUtc = supplier.LastPurchaseDateUtc,
        LastPaymentDateUtc = supplier.LastPaymentDateUtc,
        TotalPurchases = supplier.TotalPurchases,
    };
}
