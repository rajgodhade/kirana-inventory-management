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
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showInactive;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanManagePurchases => session.HasPermission(PermissionKeys.PurchasesManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<SupplierRowViewModel> Suppliers { get; } = [];

    public async Task InitializeAsync() => await SearchAsync();

    [RelayCommand]
    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await supplierService.SearchAsync(
                new SupplierSearchQuery
                {
                    SearchText = SearchText,
                    IncludeInactive = ShowInactive,
                },
                CurrentUserId);

            Suppliers.Clear();
            foreach (var supplier in results)
            {
                Suppliers.Add(ToRow(supplier));
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

    public Task<Supplier> CreateSupplierAsync(CreateSupplierRequest request) => supplierService.CreateAsync(request);

    public Task<Supplier> UpdateSupplierAsync(int supplierId, UpdateSupplierRequest request) => supplierService.UpdateAsync(supplierId, request);

    public async Task SetActiveAsync(int supplierId, bool isActive) =>
        await supplierService.SetActiveAsync(supplierId, isActive, CurrentUserId);

    public Task<Supplier?> GetSupplierAsync(int supplierId) => supplierService.GetByIdAsync(supplierId, CurrentUserId);

    private static SupplierRowViewModel ToRow(Supplier supplier) => new()
    {
        Id = supplier.Id,
        SupplierCode = supplier.SupplierCode,
        Name = supplier.Name,
        ContactPerson = supplier.ContactPerson,
        Phone = supplier.Phone,
        OutstandingBalance = supplier.OutstandingBalance,
        IsActive = supplier.IsActive,
    };
}
