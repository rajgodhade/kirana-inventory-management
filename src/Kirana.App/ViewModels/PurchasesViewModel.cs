using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Purchases list/search/filter page (PRD §28).</summary>
public sealed partial class PurchasesViewModel(IPurchaseService purchaseService, ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _outstandingOnly;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanManagePurchases => session.HasPermission(PermissionKeys.PurchasesManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<PurchaseRowViewModel> Purchases { get; } = [];

    public async Task InitializeAsync() => await SearchAsync();

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
                },
                CurrentUserId);

            Purchases.Clear();
            foreach (var purchase in results)
            {
                Purchases.Add(ToRow(purchase));
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
        PurchaseNumber = purchase.PurchaseNumber,
        SupplierName = purchase.Supplier.Name,
        DateText = purchase.PurchaseDateUtc.ToLocalTime().ToString("dd-MMM-yyyy"),
        GrandTotal = purchase.GrandTotal,
        OutstandingAmount = purchase.OutstandingAmount,
    };
}
