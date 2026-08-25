using Kirana.Application.Authentication;
using Kirana.Application.Taxation;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Taxation;

public sealed class StoreTaxIdentityServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly StoreTaxIdentityService _service;
    private readonly int _ownerId;

    public StoreTaxIdentityServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        var store = _fixture.Context.Stores.Single();
        store.Name = "Old Trade Name";
        store.OwnerName = "Owner";
        store.State = "Legacy free text";
        store.SetupCompleted = true;
        _fixture.Context.SaveChanges();
        _service = new(
            _fixture.Context,
            new PermissionEnforcer(_fixture.Context),
            new EfAuditLogger(_fixture.Context));
    }

    [Fact]
    public async Task Update_persists_normalized_identity_and_audits_old_and_new_values()
    {
        var result = await _service.UpdateAsync(new UpdateStoreTaxIdentityRequest
        {
            TradeName = "Hitu Kirana",
            LegalName = "Hitu Kirana Private Limited",
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "27",
            RegistrationType = GstRegistrationType.Regular,
            PerformedByUserId = _ownerId,
        });

        Assert.Equal("27", result.StateCode);
        Assert.Equal(GstRegistrationType.Regular, result.RegistrationType);
        var stored = await _fixture.Context.Stores.SingleAsync();
        Assert.Equal("Maharashtra", stored.State);
        var audit = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "StoreGstIdentityUpdated");
        Assert.Contains("Old Trade Name", audit.PreviousValue ?? string.Empty);
        Assert.Contains("27", audit.NewValue ?? string.Empty);
    }

    [Fact]
    public async Task Update_does_not_rewrite_persisted_historical_sale_store_snapshot()
    {
        var store = await _fixture.Context.Stores.SingleAsync();
        store.LegalName = "Original Legal Name";
        store.Gstin = "27AAPFU0939F1ZV";
        store.StateCode = "27";
        store.GstRegistrationType = GstRegistrationType.Regular;
        var sale = new Sale { InvoiceNumber = "INV-SNAPSHOT-STORE" };
        HistoricalGstIdentitySnapshotFactory.Capture(
            sale,
            store,
            customer: null,
            new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc));
        _fixture.Context.Sales.Add(sale);
        await _fixture.Context.SaveChangesAsync();

        await _service.UpdateAsync(new UpdateStoreTaxIdentityRequest
        {
            TradeName = "Changed Trade Name",
            LegalName = "Changed Legal Name",
            Gstin = "29AAACB2894G1ZJ",
            StateCode = "29",
            RegistrationType = GstRegistrationType.Composition,
            PerformedByUserId = _ownerId,
        });

        var persisted = await _fixture.Context.Sales.AsNoTracking().SingleAsync(s => s.Id == sale.Id);
        Assert.Equal("Old Trade Name", persisted.StoreTradeNameSnapshot);
        Assert.Equal("Original Legal Name", persisted.StoreLegalNameSnapshot);
        Assert.Equal("27AAPFU0939F1ZV", persisted.StoreGstinSnapshot);
        Assert.Equal("27", persisted.StoreStateCodeSnapshot);
        Assert.Equal(GstRegistrationType.Regular, persisted.StoreGstRegistrationTypeSnapshot);
    }

    [Fact]
    public async Task Update_rejects_gstin_state_mismatch_without_mutating_store()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(new UpdateStoreTaxIdentityRequest
        {
            TradeName = "Changed",
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "29",
            RegistrationType = GstRegistrationType.Regular,
            PerformedByUserId = _ownerId,
        }));

        var stored = await _fixture.Context.Stores.AsNoTracking().SingleAsync();
        Assert.Equal("Old Trade Name", stored.Name);
        Assert.Null(stored.StateCode);
    }

    [Fact]
    public async Task Read_is_side_effect_free_for_legacy_identity()
    {
        var before = await _fixture.Context.Stores.AsNoTracking().SingleAsync();
        var result = await _service.GetAsync();
        var after = await _fixture.Context.Stores.AsNoTracking().SingleAsync();

        Assert.NotNull(result);
        Assert.Null(result.StateCode);
        Assert.Equal(before.State, after.State);
        Assert.Null(after.StateCode);
        Assert.Null(after.GstRegistrationType);
        Assert.Empty(await _fixture.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Update_requires_existing_settings_permission()
    {
        var cashier = await _fixture.SeedCashierAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.UpdateAsync(new UpdateStoreTaxIdentityRequest
        {
            TradeName = "Blocked",
            PerformedByUserId = cashier.Id,
        }));
    }

    public void Dispose() => _fixture.Dispose();
}
