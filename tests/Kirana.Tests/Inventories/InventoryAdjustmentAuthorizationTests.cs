using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Infrastructure.Security;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Inventories;

/// <summary>
/// Phase 13D introduces no new permission key: a manual adjustment changes stock levels, which is
/// exactly what <see cref="PermissionKeys.InventoryManage"/> already governs (Owner + Manager,
/// withheld from Cashier). A second key for the same authority would only be free to drift.
///
/// <para>Enforcement is in the service, so hiding the navigation entry is defence in depth rather
/// than the actual control — a Cashier calling the service directly is refused.</para>
/// </summary>
public class InventoryAdjustmentAuthorizationTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly InventoryAdjustmentService _sut;
    private readonly int _ownerId;
    private readonly int _managerId;
    private readonly int _cashierId;
    private readonly int _productId;

    public InventoryAdjustmentAuthorizationTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;
        _cashierId = _fixture.SeedCashierAsync().GetAwaiter().GetResult().Id;
        _managerId = SeedManagerAsync().GetAwaiter().GetResult();

        _sut = new InventoryAdjustmentService(
            _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context));

        var product = new Product
        {
            ProductCode = "PRD-ADJGATE", Name = "Gated Product", Unit = UnitOfMeasure.Piece,
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
            Username = $"mgr-{Guid.NewGuid():N}"[..14],
            FullName = "Test Manager",
            PasswordHash = new BCryptPasswordHasher().Hash("Manager@123"),
            Role = role,
            IsActive = true,
        };
        _fixture.Context.Users.Add(manager);
        await _fixture.Context.SaveChangesAsync();
        return manager.Id;
    }

    private CreateInventoryAdjustmentRequest Request(int? userId) => new()
    {
        ProductId = _productId,
        Direction = InventoryAdjustmentDirection.Decrease,
        Quantity = 5m,
        Reason = InventoryAdjustmentReason.Damaged,
        PerformedByUserId = userId,
    };

    [Fact]
    public async Task Cashier_IsRefused()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CreateAsync(Request(_cashierId)));
    }

    /// <summary>The refusal is the whole point of service-layer enforcement: bypassing the UI must
    /// not bypass the check, and must leave nothing behind.</summary>
    [Fact]
    public async Task CashierDirectServiceCall_ChangesNothing()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CreateAsync(Request(_cashierId)));

        var stock = await _fixture.Context.Inventories
            .Where(i => i.ProductId == _productId).Select(i => i.QuantityOnHand).FirstAsync();
        Assert.Equal(100m, stock);
        Assert.Empty(await _fixture.Context.StockMovements.ToListAsync());
        Assert.Empty(await _fixture.Context.InventoryAdjustments.ToListAsync());
    }

    [Fact]
    public async Task NullUser_IsRefused()
    {
        // Billing Mode runs logged-out; it must not be able to adjust stock.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.CreateAsync(Request(null)));
    }

    [Fact]
    public async Task Manager_IsAllowed()
    {
        var adjustment = await _sut.CreateAsync(Request(_managerId));

        Assert.Equal(95m, adjustment.NewQuantity);
        Assert.Equal(_managerId, adjustment.AdjustedByUserId);
    }

    [Fact]
    public async Task Owner_IsAllowed()
    {
        var adjustment = await _sut.CreateAsync(Request(_ownerId));

        Assert.Equal(95m, adjustment.NewQuantity);
    }

    /// <summary>Pins the decision itself: granting InventoryManage to Cashier later would silently
    /// open manual stock adjustment to cashiers, so it fails a test instead.</summary>
    [Fact]
    public void CashierRole_DoesNotIncludeInventoryManage()
    {
        Assert.DoesNotContain(PermissionKeys.InventoryManage, PermissionKeys.Cashier);
        Assert.Contains(PermissionKeys.InventoryManage, PermissionKeys.Manager);
        Assert.Contains(PermissionKeys.InventoryManage, PermissionKeys.Owner);
    }

    /// <summary>Reads are not gated — the history page is a report, and previewing writes nothing.
    /// The write is the only privileged operation.</summary>
    [Fact]
    public async Task ReadsAndPreviewAreNotPermissionGated()
    {
        await _sut.CreateAsync(Request(_ownerId));

        Assert.Single(await _sut.SearchAsync(new InventoryAdjustmentQuery()));
        Assert.Equal(95m, await _sut.GetCurrentStockAsync(_productId));

        var preview = await _sut.PreviewAsync(Request(_cashierId));
        Assert.Equal(90m, preview.ResultingQuantity);

        // ...and previewing as a Cashier still applied nothing.
        Assert.Equal(95m, await _sut.GetCurrentStockAsync(_productId));
    }

    public void Dispose() => _fixture.Dispose();
}
