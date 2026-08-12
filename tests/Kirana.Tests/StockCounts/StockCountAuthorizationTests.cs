using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.StockCounts;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.StockCounts;

/// <summary>
/// Phase 13C introduces no new permission key: stock counting is gated by the existing
/// <see cref="PermissionKeys.InventoryManage"/>, which already means "may change stock levels" and
/// is already granted to Owner and Manager and withheld from Cashier. Adding a second key for the
/// same authority would let the two drift apart.
///
/// <para>Enforcement lives in the service, not the page, so a direct service call from anywhere is
/// refused exactly as the UI would be.</para>
/// </summary>
public class StockCountAuthorizationTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly StockCountService _sut;
    private readonly int _ownerId;
    private readonly int _cashierId;
    private readonly int _managerId;
    private readonly int _productId;

    public StockCountAuthorizationTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _cashierId = _fixture.SeedCashierAsync().GetAwaiter().GetResult().Id;
        _managerId = SeedManagerAsync().GetAwaiter().GetResult();

        _sut = new StockCountService(
            _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context),
            new BarcodeLookupService(_fixture.Context));

        var product = new Product
        {
            ProductCode = "PRD-SCGATE", Name = "Gated Product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10m, Mrp = 15m, SellingPrice = 14m, IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 100m });
        _fixture.Context.SaveChanges();
        _productId = product.Id;
    }

    private async Task<int> SeedManagerAsync()
    {
        var role = await _fixture.Context.Roles.SingleAsync(r => r.Name == "Manager");
        var manager = new User
        {
            Username = $"manager-{Guid.NewGuid():N}"[..14],
            FullName = "Test Manager",
            PasswordHash = new Kirana.Infrastructure.Security.BCryptPasswordHasher().Hash("Manager@123"),
            Role = role,
            IsActive = true,
        };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();
        return manager.Id;
    }

    // ---- Cashier is refused at every mutation ----

    [Fact]
    public async Task StartAsync_RequiresInventoryManage()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.StartAsync(null, _cashierId));
    }

    [Fact]
    public async Task AddItemAsync_RequiresInventoryManage()
    {
        var count = await _sut.StartAsync(null, _ownerId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.AddItemAsync(count.Id, _productId, null, _cashierId));
    }

    [Fact]
    public async Task AddItemByBarcodeAsync_RequiresInventoryManage()
    {
        var count = await _sut.StartAsync(null, _ownerId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.AddItemByBarcodeAsync(count.Id, "ANY-CODE", _cashierId));
    }

    [Fact]
    public async Task SetCountedQuantityAsync_RequiresInventoryManage()
    {
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, _productId, null, _ownerId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.SetCountedQuantityAsync(item.Id, 90m, null, _cashierId));
    }

    [Fact]
    public async Task RemoveItemAsync_RequiresInventoryManage()
    {
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, _productId, null, _ownerId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.RemoveItemAsync(item.Id, _cashierId));
    }

    [Fact]
    public async Task SetNotesAsync_RequiresInventoryManage()
    {
        var count = await _sut.StartAsync(null, _ownerId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.SetNotesAsync(count.Id, "x", _cashierId));
    }

    [Fact]
    public async Task CancelAsync_RequiresInventoryManage()
    {
        var count = await _sut.StartAsync(null, _ownerId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CancelAsync(count.Id, null, _cashierId));
    }

    /// <summary>The most consequential gate: finalization is the one call that moves stock.</summary>
    [Fact]
    public async Task FinalizeAsync_RequiresInventoryManage_AndAppliesNothingWhenRefused()
    {
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, _productId, null, _ownerId);
        await _sut.SetCountedQuantityAsync(item.Id, 90m, null, _ownerId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.FinalizeAsync(count.Id, _cashierId));

        var stock = await _fixture.Context.Inventories
            .Where(i => i.ProductId == _productId).Select(i => i.QuantityOnHand).FirstAsync();
        Assert.Equal(100m, stock);
        Assert.Empty(await _fixture.Context.StockMovements.ToListAsync());
        var reloaded = await _fixture.Context.StockCounts.FirstAsync(c => c.Id == count.Id);
        Assert.Equal(StockCountStatus.InProgress, reloaded.Status);
    }

    // ---- Owner and Manager may perform the full workflow ----

    [Fact]
    public async Task OwnerCanRunTheWholeWorkflow()
    {
        var count = await _sut.StartAsync(null, _ownerId);
        var item = await _sut.AddItemAsync(count.Id, _productId, null, _ownerId);
        await _sut.SetCountedQuantityAsync(item.Id, 97m, null, _ownerId);
        var result = await _sut.FinalizeAsync(count.Id, _ownerId);

        Assert.Equal(1, result.DecreasedCount);
    }

    [Fact]
    public async Task ManagerCanRunTheWholeWorkflow()
    {
        // Manager holds InventoryManage by default, so stock counting is available to them without
        // any new grant — matching how they already manage stock adjustments.
        var count = await _sut.StartAsync(null, _managerId);
        var item = await _sut.AddItemAsync(count.Id, _productId, null, _managerId);
        await _sut.SetCountedQuantityAsync(item.Id, 103m, null, _managerId);
        var result = await _sut.FinalizeAsync(count.Id, _managerId);

        Assert.Equal(1, result.IncreasedCount);
    }

    /// <summary>Guards the decision itself: if InventoryManage were ever added to the Cashier
    /// defaults, stock counting would silently open up to cashiers. This pins the intent.</summary>
    [Fact]
    public void CashierRole_DoesNotIncludeInventoryManage()
    {
        Assert.DoesNotContain(PermissionKeys.InventoryManage, PermissionKeys.Cashier);
        Assert.Contains(PermissionKeys.InventoryManage, PermissionKeys.Manager);
        Assert.Contains(PermissionKeys.InventoryManage, PermissionKeys.Owner);
    }

    /// <summary>Reads are not gated: the count list and detail are visible to anyone who reached the
    /// management area, matching how other read-only history screens behave.</summary>
    [Fact]
    public async Task ReadsAreNotPermissionGated()
    {
        await _sut.StartAsync(null, _ownerId);

        Assert.NotNull(await _sut.GetActiveAsync());
        Assert.Single(await _sut.GetSummariesAsync());
    }

    public void Dispose() => _fixture.Dispose();
}
