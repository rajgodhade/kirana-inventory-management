using Kirana.Application.Customers;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Customers;

public class CustomerServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CustomerService _sut;

    public CustomerServiceTests()
    {
        _sut = new CustomerService(_fixture.Context, new EfAuditLogger(_fixture.Context));
    }

    [Fact]
    public async Task CreateAsync_CreatesCustomer()
    {
        var customer = await _sut.CreateAsync("Ramesh Kumar", "9876543210", "MG Road", null, performedByUserId: null);

        Assert.Equal("Ramesh Kumar", customer.Name);
        Assert.Equal("9876543210", customer.Phone);
        Assert.True(customer.IsActive);
        Assert.Equal(0m, customer.CreditBalance);
    }

    [Fact]
    public async Task CreateAsync_Throws_WhenNameMissing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreateAsync("   ", null, null, null, null));
    }

    [Fact]
    public async Task CreateAsync_Throws_OnDuplicatePhone()
    {
        await _sut.CreateAsync("First Customer", "9999999999", null, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateAsync("Second Customer", "9999999999", null, null, null));
    }

    [Fact]
    public async Task CreateAsync_AllowsMultipleCustomers_WithoutPhone()
    {
        await _sut.CreateAsync("Walk-in A", null, null, null, null);
        await _sut.CreateAsync("Walk-in B", null, null, null, null);

        var all = await _sut.SearchAsync(null);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task SearchAsync_FindsCustomerByNameOrPhone()
    {
        await _sut.CreateAsync("Suresh Patel", "9123456789", null, null, null);

        var byName = await _sut.SearchAsync("Suresh");
        var byPhone = await _sut.SearchAsync("9123456789");

        Assert.Single(byName);
        Assert.Single(byPhone);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        Assert.Null(await _sut.GetByIdAsync(999));
    }

    public void Dispose() => _fixture.Dispose();
}
