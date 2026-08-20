using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Kirana.Domain.Taxation;

namespace Kirana.Tests.Purchasing;

public class SupplierServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly SupplierService _sut;
    private readonly int _ownerId;

    public SupplierServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var sequenceGenerator = new EfSequenceGenerator(_fixture.Context);
        var auditLogger = new EfAuditLogger(_fixture.Context);
        var permissionEnforcer = new PermissionEnforcer(_fixture.Context);
        _sut = new SupplierService(_fixture.Context, sequenceGenerator, auditLogger, permissionEnforcer);
    }

    private CreateSupplierRequest ValidRequest(string name = "Sharma Distributors", string? phone = "9876500001") => new()
    {
        Name = name,
        Phone = phone,
        Gstin = "27AAPFU0939F1ZV",
        ContactPerson = "Ramesh",
        Email = "ramesh@sharma.example",
        Address = "MG Road",
        PerformedByUserId = _ownerId,
    };

    [Fact]
    public async Task CreateAsync_GeneratesSequentialSupplierCodes()
    {
        var first = await _sut.CreateAsync(ValidRequest("First", "9000000001"));
        var second = await _sut.CreateAsync(ValidRequest("Second", "9000000002"));

        Assert.Equal("SUP-000001", first.SupplierCode);
        Assert.Equal("SUP-000002", second.SupplierCode);
    }

    [Fact]
    public async Task CreateAsync_SetsSupplierActiveByDefault()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());

        Assert.True(supplier.IsActive);
        Assert.Equal(0m, supplier.OutstandingBalance);
    }

    [Fact]
    public async Task CreateAsync_PersistsGstIdentity()
    {
        var supplier = await _sut.CreateAsync(new CreateSupplierRequest
        {
            Name = "Registered Supplier",
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "27",
            GstRegistrationType = GstRegistrationType.Regular,
            PerformedByUserId = _ownerId,
        });

        Assert.Equal("27", supplier.StateCode);
        Assert.Equal(GstRegistrationType.Regular, supplier.GstRegistrationType);
    }

    [Fact]
    public async Task CreateAsync_RejectsGstinStateMismatch()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(new CreateSupplierRequest
        {
            Name = "Wrong State",
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "29",
            GstRegistrationType = GstRegistrationType.Regular,
            PerformedByUserId = _ownerId,
        }));
        Assert.Empty(await _fixture.Context.Suppliers.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenNameMissing()
    {
        var request = new CreateSupplierRequest { Name = "   ", PerformedByUserId = _ownerId };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(request));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPerformerLacksPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.CreateAsync(new CreateSupplierRequest { Name = "X", PerformedByUserId = cashier.Id }));
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenPerformerIsNull()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.CreateAsync(new CreateSupplierRequest { Name = "X", PerformedByUserId = null }));
    }

    [Fact]
    public async Task CreateAsync_LogsAuditEntry()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());

        var entry = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "SupplierCreated");
        Assert.Equal(supplier.Id.ToString(), entry.EntityId);
        Assert.Equal(_ownerId, entry.UserId);
    }

    [Fact]
    public async Task UpdateAsync_AppliesChanges()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());

        var updated = await _sut.UpdateAsync(supplier.Id, new UpdateSupplierRequest
        {
            Name = "Renamed Distributors",
            Phone = "9999999999",
            PerformedByUserId = _ownerId,
        });

        Assert.Equal("Renamed Distributors", updated.Name);
        Assert.Equal("9999999999", updated.Phone);
    }

    [Fact]
    public async Task UpdateAsync_ChangesGstIdentityAndCreatesDistinctAudit()
    {
        var supplier = await _sut.CreateAsync(new CreateSupplierRequest
        {
            Name = "Tax Supplier", PerformedByUserId = _ownerId,
        });

        var updated = await _sut.UpdateAsync(supplier.Id, new UpdateSupplierRequest
        {
            Name = supplier.Name,
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "27",
            GstRegistrationType = GstRegistrationType.Composition,
            PerformedByUserId = _ownerId,
        });

        Assert.Equal(GstRegistrationType.Composition, updated.GstRegistrationType);
        var audit = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "SupplierGstIdentityUpdated");
        Assert.Contains("Not specified", audit.PreviousValue ?? string.Empty);
        Assert.Contains("Composition", audit.NewValue ?? string.Empty);
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenSupplierNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateAsync(999, new UpdateSupplierRequest { Name = "X", PerformedByUserId = _ownerId }));
    }

    [Fact]
    public async Task SetActiveAsync_TogglesActiveFlag()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());

        await _sut.SetActiveAsync(supplier.Id, isActive: false, _ownerId);
        Assert.False((await _sut.GetByIdAsync(supplier.Id, _ownerId))!.IsActive);

        await _sut.SetActiveAsync(supplier.Id, isActive: true, _ownerId);
        Assert.True((await _sut.GetByIdAsync(supplier.Id, _ownerId))!.IsActive);
    }

    [Fact]
    public async Task SearchAsync_ExactSupplierCodeMatch_IsFound()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());

        var results = await _sut.SearchAsync(new SupplierSearchQuery { SearchText = supplier.SupplierCode }, _ownerId);

        Assert.Single(results);
        Assert.Equal(supplier.Id, results[0].Id);
    }

    [Fact]
    public async Task SearchAsync_PartialNameMatch_IsFound()
    {
        await _sut.CreateAsync(ValidRequest("Amul Distributors", "9111111111"));
        await _sut.CreateAsync(ValidRequest("Tata Traders", "9222222222"));

        var results = await _sut.SearchAsync(new SupplierSearchQuery { SearchText = "Distrib" }, _ownerId);

        Assert.Single(results);
        Assert.Equal("Amul Distributors", results[0].Name);
    }

    [Fact]
    public async Task SearchAsync_ExcludesInactiveSuppliers_ByDefault()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());
        await _sut.SetActiveAsync(supplier.Id, isActive: false, _ownerId);

        var results = await _sut.SearchAsync(new SupplierSearchQuery { SearchText = supplier.Name }, _ownerId);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchOverviewAsync_ReturnsExistingPurchaseAndPaymentFacts()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());
        var purchaseDate = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var paymentDate = purchaseDate.AddDays(2);
        _fixture.Context.Purchases.Add(new Purchase
        {
            PurchaseNumber = "PUR-TEST-001",
            SupplierId = supplier.Id,
            PurchaseDateUtc = purchaseDate,
            GrandTotal = 500m,
            OutstandingAmount = 300m,
        });
        _fixture.Context.SupplierPayments.Add(new SupplierPayment
        {
            SupplierId = supplier.Id,
            Amount = 200m,
            PaymentDateUtc = paymentDate,
        });
        supplier.OutstandingBalance = 300m;
        await _fixture.Context.SaveChangesAsync();

        var overview = Assert.Single(await _sut.SearchOverviewAsync(
            new SupplierSearchQuery { SearchText = supplier.SupplierCode }, _ownerId));

        Assert.Equal(500m, overview.TotalPurchases);
        Assert.Equal(purchaseDate, overview.LastPurchaseDateUtc);
        Assert.Equal(paymentDate, overview.LastPaymentDateUtc);
        Assert.Equal(300m, overview.OutstandingBalance);
    }

    [Fact]
    public async Task SearchAsync_Throws_WhenReaderLacksPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();

        // Supplier records carry OutstandingBalance, so even reading them is gated.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.SearchAsync(new SupplierSearchQuery(), cashier.Id));
    }

    [Fact]
    public async Task GetByIdAsync_Throws_WhenReaderLacksPermission()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetByIdAsync(supplier.Id, cashier.Id));
    }

    [Fact]
    public async Task GetLedgerAsync_Throws_WhenReaderLacksPermission()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetLedgerAsync(supplier.Id, cashier.Id));
    }

    [Fact]
    public async Task GetLedgerAsync_Throws_WhenReaderIsNull()
    {
        var supplier = await _sut.CreateAsync(ValidRequest());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.GetLedgerAsync(supplier.Id, performedByUserId: null));
    }

    public void Dispose() => _fixture.Dispose();
}
