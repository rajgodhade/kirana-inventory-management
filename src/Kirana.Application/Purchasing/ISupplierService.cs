using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

/// <summary>
/// Supplier master-data management (PRD §28-29): CRUD, search, and the supplier ledger. Every
/// method — reads included — requires <see cref="PermissionKeys.PurchasesManage"/>, because a
/// supplier record carries its outstanding balance and the ledger is pure financial data
/// (PRD §6, §9: financial figures are gated at the service layer, not just hidden in the UI).
/// </summary>
public interface ISupplierService
{
    Task<Supplier> CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken = default);

    Task<Supplier> UpdateAsync(int supplierId, UpdateSupplierRequest request, CancellationToken cancellationToken = default);

    Task SetActiveAsync(int supplierId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<Supplier?> GetByIdAsync(int supplierId, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Supplier>> SearchAsync(SupplierSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Read-only supplier rows enriched with existing purchase/payment history for the supplier list.</summary>
    Task<IReadOnlyList<SupplierOverview>> SearchOverviewAsync(SupplierSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>All purchases and payments for this supplier, merged and sorted chronologically
    /// with a running balance (PRD §29).</summary>
    Task<IReadOnlyList<SupplierLedgerEntry>> GetLedgerAsync(int supplierId, int? performedByUserId, CancellationToken cancellationToken = default);
}
