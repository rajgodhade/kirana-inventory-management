using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Purchasing;

public sealed class SupplierService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer)
    : ISupplierService
{
    private const string SupplierSequenceKey = "Supplier";
    private const string SupplierCodePrefix = "SUP";
    private const int SupplierCodePadding = 6;

    public async Task<Supplier> CreateAsync(CreateSupplierRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        var name = RequireName(request.Name);

        var supplierCode = await sequenceGenerator.NextAsync(SupplierSequenceKey, SupplierCodePrefix, SupplierCodePadding, cancellationToken);

        var supplier = new Supplier
        {
            SupplierCode = supplierCode,
            Name = name,
            Gstin = Normalize(request.Gstin),
            ContactPerson = Normalize(request.ContactPerson),
            Phone = Normalize(request.Phone),
            Email = Normalize(request.Email),
            Address = Normalize(request.Address),
            IsActive = true,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(request.PerformedByUserId, "SupplierCreated", nameof(Supplier), supplier.Id.ToString(),
            newValue: $"{supplier.SupplierCode} - {supplier.Name}", cancellationToken: cancellationToken);

        return supplier;
    }

    public async Task<Supplier> UpdateAsync(int supplierId, UpdateSupplierRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");

        supplier.Name = RequireName(request.Name);
        supplier.Gstin = Normalize(request.Gstin);
        supplier.ContactPerson = Normalize(request.ContactPerson);
        supplier.Phone = Normalize(request.Phone);
        supplier.Email = Normalize(request.Email);
        supplier.Address = Normalize(request.Address);
        supplier.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(request.PerformedByUserId, "SupplierUpdated", nameof(Supplier), supplier.Id.ToString(),
            cancellationToken: cancellationToken);

        return supplier;
    }

    public async Task SetActiveAsync(int supplierId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");

        if (supplier.IsActive == isActive)
        {
            return;
        }

        supplier.IsActive = isActive;
        supplier.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(performedByUserId, isActive ? "SupplierReactivated" : "SupplierDeactivated",
            nameof(Supplier), supplier.Id.ToString(), cancellationToken: cancellationToken);
    }

    public async Task<Supplier?> GetByIdAsync(int supplierId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        return await db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);
    }

    public async Task<IReadOnlyList<Supplier>> SearchAsync(
        SupplierSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        var suppliers = db.Suppliers.AsQueryable();
        if (!query.IncludeInactive)
        {
            suppliers = suppliers.Where(s => s.IsActive);
        }

        var text = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var likeText = $"%{text}%";
            suppliers = suppliers.Where(s =>
                s.SupplierCode == text ||
                EF.Functions.Like(s.Name, likeText) ||
                (s.Phone != null && EF.Functions.Like(s.Phone, likeText)));
        }

        return await suppliers.OrderBy(s => s.Name).Take(query.MaxResults).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierLedgerEntry>> GetLedgerAsync(
        int supplierId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        var purchases = await db.Purchases
            .Where(p => p.SupplierId == supplierId)
            .OrderBy(p => p.PurchaseDateUtc)
            .ToListAsync(cancellationToken);

        var payments = await db.SupplierPayments
            .Where(p => p.SupplierId == supplierId)
            .OrderBy(p => p.PaymentDateUtc)
            .ToListAsync(cancellationToken);

        var events = purchases
            .Select(p => (Date: p.PurchaseDateUtc, IsPurchase: true, Purchase: (Purchase?)p, Payment: (SupplierPayment?)null))
            .Concat(payments.Select(p => (Date: p.PaymentDateUtc, IsPurchase: false, Purchase: (Purchase?)null, Payment: (SupplierPayment?)p)))
            .OrderBy(e => e.Date)
            .ToList();

        var runningBalance = 0m;
        var entries = new List<SupplierLedgerEntry>();
        foreach (var entry in events)
        {
            if (entry.IsPurchase)
            {
                var purchase = entry.Purchase!;
                runningBalance += purchase.GrandTotal;
                entries.Add(new SupplierLedgerEntry
                {
                    DateUtc = purchase.PurchaseDateUtc,
                    EntryType = "Purchase",
                    Reference = purchase.PurchaseNumber,
                    DebitAmount = purchase.GrandTotal,
                    RunningBalance = runningBalance,
                    Notes = purchase.Notes,
                });
            }
            else
            {
                var payment = entry.Payment!;
                runningBalance -= payment.Amount;
                entries.Add(new SupplierLedgerEntry
                {
                    DateUtc = payment.PaymentDateUtc,
                    EntryType = "Payment",
                    Reference = payment.ReferenceNumber ?? payment.Method.ToString(),
                    CreditAmount = payment.Amount,
                    RunningBalance = runningBalance,
                    Notes = payment.Notes,
                });
            }
        }

        return entries;
    }

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Supplier name is required.");
        }

        return name.Trim();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
