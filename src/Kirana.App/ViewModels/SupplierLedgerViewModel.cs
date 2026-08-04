using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Supplier Ledger screen (PRD §29) — purchases, payments, and running
/// outstanding balance for one supplier.</summary>
public sealed partial class SupplierLedgerViewModel(
    int supplierId, ISupplierService supplierService, IPurchaseService purchaseService, ManagementSession session) : ObservableObject
{
    public bool CanManagePurchases => session.HasPermission(PermissionKeys.PurchasesManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public int SupplierId => supplierId;

    [ObservableProperty]
    private string _supplierName = string.Empty;

    [ObservableProperty]
    private string _supplierCode = string.Empty;

    [ObservableProperty]
    private decimal _outstandingBalance;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<SupplierLedgerRowViewModel> Entries { get; } = [];

    public async Task InitializeAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var supplier = await supplierService.GetByIdAsync(supplierId, CurrentUserId)
                ?? throw new InvalidOperationException("Supplier not found.");

            SupplierName = supplier.Name;
            SupplierCode = supplier.SupplierCode;
            OutstandingBalance = supplier.OutstandingBalance;

            var ledger = await supplierService.GetLedgerAsync(supplierId, CurrentUserId);

            Entries.Clear();
            foreach (var entry in ledger.OrderByDescending(e => e.DateUtc))
            {
                Entries.Add(new SupplierLedgerRowViewModel
                {
                    DateText = entry.DateUtc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt"),
                    EntryType = entry.EntryType,
                    Reference = entry.Reference,
                    DebitText = entry.DebitAmount > 0 ? $"₹{entry.DebitAmount:0.00}" : "",
                    CreditText = entry.CreditAmount > 0 ? $"₹{entry.CreditAmount:0.00}" : "",
                    RunningBalanceText = $"₹{entry.RunningBalance:0.00}",
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

    public async Task<bool> RecordPaymentAsync(decimal amount, PaymentMethod method, string? referenceNumber, string? notes)
    {
        ErrorMessage = null;
        try
        {
            await purchaseService.RecordPaymentAsync(new RecordSupplierPaymentRequest
            {
                SupplierId = supplierId,
                Amount = amount,
                Method = method,
                ReferenceNumber = referenceNumber,
                Notes = notes,
                RecordedByUserId = CurrentUserId,
            });
            await InitializeAsync();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }
}
