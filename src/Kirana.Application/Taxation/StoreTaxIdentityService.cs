using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Taxation;

public sealed class StoreTaxIdentityService(
    IKiranaDbContext db,
    IPermissionEnforcer permissionEnforcer,
    IAuditLogger auditLogger) : IStoreTaxIdentityService
{
    public async Task<StoreTaxIdentity?> GetAsync(CancellationToken cancellationToken = default)
    {
        return await db.Stores.AsNoTracking()
            .Select(store => new StoreTaxIdentity(
                store.Name, store.LegalName, store.Gstin, store.StateCode, store.GstRegistrationType))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<StoreTaxIdentity> UpdateAsync(
        UpdateStoreTaxIdentityRequest request,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(
            request.PerformedByUserId, PermissionKeys.SettingsChange, cancellationToken);

        if (string.IsNullOrWhiteSpace(request.TradeName))
        {
            throw new ArgumentException("Store trade name is required.");
        }

        GstinValidator.EnsureValidForWrite(request.Gstin, request.StateCode);

        var store = await db.Stores.FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Store profile is not initialized.");
        var previous = Describe(store);

        store.Name = request.TradeName.Trim();
        store.LegalName = Normalize(request.LegalName);
        store.Gstin = Normalize(request.Gstin);
        store.StateCode = Normalize(request.StateCode);
        store.GstRegistrationType = request.RegistrationType;
        store.State = IndianGstStateCatalog.FindByCode(store.StateCode)?.Name ?? store.State;
        store.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.PerformedByUserId,
            "StoreGstIdentityUpdated",
            nameof(Store),
            store.Id.ToString(),
            previous,
            Describe(store),
            cancellationToken: cancellationToken);

        return new(store.Name, store.LegalName, store.Gstin, store.StateCode, store.GstRegistrationType);
    }

    private static string Describe(Store store) =>
        $"Trade name: {store.Name}; legal name: {store.LegalName ?? "Not set"}; GSTIN: {store.Gstin ?? "Not set"}; " +
        $"state code: {store.StateCode ?? "Not set"}; registration: {store.GstRegistrationType?.ToString() ?? "Not specified"}";

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
