using Kirana.Application.Customers;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Kirana.Domain.Taxation;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Customers;

/// <summary>
/// Covers customer CRUD, Customer ID generation and POS-facing search. These paths are deliberately
/// not permission-gated (walk-in billing runs logged-out, PRD §4) — the gating that does exist lives
/// on the financial surfaces and is proved in <see cref="CustomersAuthorizationTests"/>.
/// </summary>
public class CustomerServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CustomerService _sut;

    public CustomerServiceTests()
    {
        _sut = new CustomerService(
            _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context));
    }

    private Task<Kirana.Domain.Entities.Customer> CreateAsync(
        string name = "Ramesh Kumar", string? phone = null, string? address = null,
        string? gstin = null, string? notes = null) =>
        _sut.CreateAsync(new CreateCustomerRequest
        {
            Name = name, Phone = phone, Address = address, Gstin = gstin, Notes = notes,
        });

    // ---------- creation ----------

    [Fact]
    public async Task CreateAsync_CreatesCustomer()
    {
        var customer = await CreateAsync("Ramesh Kumar", "9876543210", "MG Road", notes: "Prefers evening delivery");

        Assert.Equal("Ramesh Kumar", customer.Name);
        Assert.Equal("9876543210", customer.Phone);
        Assert.Equal("Prefers evening delivery", customer.Notes);
        Assert.True(customer.IsActive);
        Assert.Equal(0m, customer.CreditBalance);
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenNameMissing() =>
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync("   "));

    [Fact]
    public async Task CreateAsync_Throws_OnDuplicatePhone()
    {
        await CreateAsync("First Customer", "9999999999");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateAsync("Second Customer", "9999999999"));
    }

    [Fact]
    public async Task CreateAsync_AllowsMultipleCustomers_WithoutPhone()
    {
        await CreateAsync("Walk-in A");
        await CreateAsync("Walk-in B");

        Assert.Equal(2, (await _sut.SearchAsync(new CustomerSearchQuery())).Count);
    }

    [Fact]
    public async Task CreateAsync_PersistsGstIdentity()
    {
        var customer = await _sut.CreateAsync(new CreateCustomerRequest
        {
            Name = "Registered Customer",
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "27",
            GstRegistrationType = GstRegistrationType.Regular,
        });

        Assert.Equal("27", customer.StateCode);
        Assert.Equal(GstRegistrationType.Regular, customer.GstRegistrationType);
    }

    [Fact]
    public async Task CreateAsync_RejectsGstinStateMismatch()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync(new CreateCustomerRequest
        {
            Name = "Wrong State",
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "29",
            GstRegistrationType = GstRegistrationType.Regular,
        }));
        Assert.Empty(await _fixture.Context.Customers.ToListAsync());
    }

    // ---------- Customer ID generation ----------

    [Fact]
    public async Task CreateAsync_GeneratesSequentialCustomerCodes()
    {
        var first = await CreateAsync("First");
        var second = await CreateAsync("Second");
        var third = await CreateAsync("Third");

        Assert.Equal("CUST-000001", first.CustomerCode);
        Assert.Equal("CUST-000002", second.CustomerCode);
        Assert.Equal("CUST-000003", third.CustomerCode);
    }

    [Fact]
    public async Task CreateAsync_CustomerCodesAreUnique_AcrossManyCustomers()
    {
        for (var i = 0; i < 25; i++)
        {
            await CreateAsync($"Customer {i}");
        }

        var codes = await _fixture.Context.Customers.Select(c => c.CustomerCode).ToListAsync();
        Assert.Equal(25, codes.Distinct().Count());
        Assert.DoesNotContain(codes, string.IsNullOrWhiteSpace);
    }

    // ---------- update ----------

    [Fact]
    public async Task UpdateAsync_ChangesEditableFields()
    {
        var customer = await CreateAsync("Old Name", "9000000001");

        var updated = await _sut.UpdateAsync(customer.Id, new UpdateCustomerRequest
        {
            Name = "New Name", Phone = "9000000002", Address = "New Address",
            Gstin = "27AAPFU0939F1ZV", Notes = "Updated note",
        });

        Assert.Equal("New Name", updated.Name);
        Assert.Equal("9000000002", updated.Phone);
        Assert.Equal("Updated note", updated.Notes);
    }

    [Fact]
    public async Task UpdateAsync_ChangesGstIdentityAndCreatesDistinctAudit()
    {
        var customer = await CreateAsync("Tax Customer");
        var updated = await _sut.UpdateAsync(customer.Id, new UpdateCustomerRequest
        {
            Name = customer.Name,
            Gstin = "27AAPFU0939F1ZV",
            StateCode = "27",
            GstRegistrationType = GstRegistrationType.Composition,
        });

        Assert.Equal(GstRegistrationType.Composition, updated.GstRegistrationType);
        var audit = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "CustomerGstIdentityUpdated");
        Assert.Contains("Not specified", audit.PreviousValue ?? string.Empty);
        Assert.Contains("Composition", audit.NewValue ?? string.Empty);
    }

    [Fact]
    public async Task UpdateAsync_NeverChangesCustomerCode()
    {
        var customer = await CreateAsync("Stable Code");
        var originalCode = customer.CustomerCode;

        var updated = await _sut.UpdateAsync(customer.Id, new UpdateCustomerRequest { Name = "Renamed" });

        Assert.Equal(originalCode, updated.CustomerCode);
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenPhoneBelongsToAnotherCustomer()
    {
        await CreateAsync("Owner Of Number", "9111111111");
        var other = await CreateAsync("Other Customer", "9222222222");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateAsync(other.Id, new UpdateCustomerRequest { Name = "Other Customer", Phone = "9111111111" }));
    }

    [Fact]
    public async Task UpdateAsync_AllowsKeepingOwnPhone()
    {
        var customer = await CreateAsync("Keeps Number", "9333333333");

        var updated = await _sut.UpdateAsync(customer.Id, new UpdateCustomerRequest
        {
            Name = "Keeps Number Renamed", Phone = "9333333333",
        });

        Assert.Equal("9333333333", updated.Phone);
    }

    // ---------- active status ----------

    [Fact]
    public async Task SetActiveAsync_DeactivatesAndReactivates()
    {
        var customer = await CreateAsync("Toggle Me");

        Assert.False((await _sut.SetActiveAsync(customer.Id, isActive: false)).IsActive);
        Assert.True((await _sut.SetActiveAsync(customer.Id, isActive: true)).IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_Throws_WhenDeactivatingCustomerWithOutstandingBalance()
    {
        var customer = await CreateAsync("Owes Money");
        customer.CreditBalance = 250m;
        await _fixture.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SetActiveAsync(customer.Id, isActive: false));
        Assert.Contains("outstanding", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- search ----------

    [Fact]
    public async Task SearchAsync_FindsCustomerByNameOrPhone()
    {
        await CreateAsync("Suresh Patel", "9123456789");

        Assert.Single(await _sut.SearchAsync(new CustomerSearchQuery { SearchText = "Suresh" }));
        Assert.Single(await _sut.SearchAsync(new CustomerSearchQuery { SearchText = "9123456789" }));
    }

    [Fact]
    public async Task SearchAsync_FindsCustomerByCustomerCode()
    {
        var customer = await CreateAsync("Code Lookup");

        var results = await _sut.SearchAsync(new CustomerSearchQuery { SearchText = customer.CustomerCode });

        Assert.Equal(customer.Id, Assert.Single(results).Id);
    }

    [Fact]
    public async Task SearchAsync_PrioritisesExactCodeMatch()
    {
        // "CUST-000001" is also a partial match for nothing else, so seed a decoy whose *name*
        // contains the code to prove exact-code matches are ranked ahead of partial name matches.
        var target = await CreateAsync("Target");
        await CreateAsync($"Mentions {target.CustomerCode} in name");

        var results = await _sut.SearchAsync(new CustomerSearchQuery { SearchText = target.CustomerCode });

        Assert.Equal(target.Id, results[0].Id);
    }

    [Fact]
    public async Task SearchAsync_ExcludesInactiveCustomers_ByDefault()
    {
        var customer = await CreateAsync("Gone Away");
        await _sut.SetActiveAsync(customer.Id, isActive: false);

        Assert.Empty(await _sut.SearchAsync(new CustomerSearchQuery { SearchText = "Gone Away" }));
        Assert.Single(await _sut.SearchAsync(new CustomerSearchQuery { SearchText = "Gone Away", IncludeInactive = true }));
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        for (var i = 0; i < 10; i++)
        {
            await CreateAsync($"Bulk Customer {i}");
        }

        var results = await _sut.SearchAsync(new CustomerSearchQuery { SearchText = "Bulk", MaxResults = 4 });

        Assert.Equal(4, results.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound() => Assert.Null(await _sut.GetByIdAsync(999));

    // ---------- audit ----------

    [Fact]
    public async Task CreateAsync_WritesAuditEntry()
    {
        var owner = await _fixture.SeedOwnerAsync();

        var customer = await _sut.CreateAsync(new CreateCustomerRequest
        {
            Name = "Audited Customer", PerformedByUserId = owner.Id,
        });

        var audit = await _fixture.Context.AuditLogs.SingleOrDefaultAsync(
            a => a.Action == "CustomerCreated" && a.EntityId == customer.Id.ToString());
        Assert.NotNull(audit);
        Assert.Equal(owner.Id, audit!.UserId);
    }

    [Fact]
    public async Task UpdateAsync_WritesAuditEntry()
    {
        var customer = await CreateAsync("Before Audit");

        await _sut.UpdateAsync(customer.Id, new UpdateCustomerRequest { Name = "After Audit" });

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(
            a => a.Action == "CustomerUpdated" && a.EntityId == customer.Id.ToString()));
    }

    [Fact]
    public async Task SetActiveAsync_WritesDistinctAuditActions()
    {
        var customer = await CreateAsync("Audit Toggle");

        await _sut.SetActiveAsync(customer.Id, isActive: false);
        await _sut.SetActiveAsync(customer.Id, isActive: true);

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "CustomerDeactivated"));
        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "CustomerReactivated"));
    }

    public void Dispose() => _fixture.Dispose();
}
