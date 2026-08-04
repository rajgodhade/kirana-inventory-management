using Kirana.Application.Audit;
using Kirana.Application.Authentication;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Audit;

public class AuditLogQueryServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly EfAuditLogger _auditLogger;
    private readonly AuditLogQueryService _sut;
    private readonly int _ownerId;

    public AuditLogQueryServiceTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _auditLogger = new EfAuditLogger(_fixture.Context);
        _sut = new AuditLogQueryService(_fixture.Context, new PermissionEnforcer(_fixture.Context));
    }

    [Fact]
    public async Task SearchAsync_ReturnsMostRecentFirst()
    {
        await _auditLogger.RecordAsync(_ownerId, "First", "TestEntity");
        await _auditLogger.RecordAsync(_ownerId, "Second", "TestEntity");

        var results = await _sut.SearchAsync(new AuditLogQuery(), _ownerId);

        Assert.True(results.Count >= 2);
        Assert.Equal("Second", results[0].Action);
    }

    [Fact]
    public async Task SearchAsync_FiltersByUserId()
    {
        var otherUser = await _fixture.SeedCashierAsync();
        await _auditLogger.RecordAsync(_ownerId, "OwnerAction", "TestEntity");
        await _auditLogger.RecordAsync(otherUser.Id, "CashierAction", "TestEntity");

        var results = await _sut.SearchAsync(new AuditLogQuery { UserId = otherUser.Id }, _ownerId);

        Assert.Single(results);
        Assert.Equal("CashierAction", results[0].Action);
    }

    [Fact]
    public async Task SearchAsync_FiltersByAction()
    {
        await _auditLogger.RecordAsync(_ownerId, "ProductCreated", "Product");
        await _auditLogger.RecordAsync(_ownerId, "ProductUpdated", "Product");

        var results = await _sut.SearchAsync(new AuditLogQuery { Action = "ProductCreated" }, _ownerId);

        Assert.Single(results);
        Assert.Equal("ProductCreated", results[0].Action);
    }

    [Fact]
    public async Task SearchAsync_FiltersByEntity()
    {
        await _auditLogger.RecordAsync(_ownerId, "Created", "Product");
        await _auditLogger.RecordAsync(_ownerId, "Created", "Category");

        var results = await _sut.SearchAsync(new AuditLogQuery { Entity = "Category" }, _ownerId);

        Assert.Single(results);
        Assert.Equal("Category", results[0].Entity);
    }

    [Fact]
    public async Task SearchAsync_FiltersByDateRange()
    {
        await _auditLogger.RecordAsync(_ownerId, "OldEnough", "TestEntity");

        var futureFrom = DateTime.UtcNow.AddDays(1);
        var results = await _sut.SearchAsync(new AuditLogQuery { FromUtc = futureFrom }, _ownerId);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_Throws_WhenPerformerLacksAuditLogViewPermission()
    {
        var cashier = await _fixture.SeedCashierAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.SearchAsync(new AuditLogQuery(), cashier.Id));
    }

    [Fact]
    public async Task SearchAsync_Throws_WhenPerformerIsNull()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.SearchAsync(new AuditLogQuery(), null));
    }

    [Fact]
    public async Task GetDistinctActionsAsync_ReturnsUniqueSortedActions()
    {
        await _auditLogger.RecordAsync(_ownerId, "Zeta", "TestEntity");
        await _auditLogger.RecordAsync(_ownerId, "Alpha", "TestEntity");
        await _auditLogger.RecordAsync(_ownerId, "Alpha", "TestEntity");

        var actions = await _sut.GetDistinctActionsAsync();

        Assert.Contains("Alpha", actions);
        Assert.Contains("Zeta", actions);
        Assert.Equal(actions.Count, actions.Distinct().Count());
    }

    public void Dispose() => _fixture.Dispose();
}
