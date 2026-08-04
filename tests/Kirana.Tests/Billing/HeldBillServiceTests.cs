using Kirana.Application.Billing;
using Kirana.Domain.Entities;
using Kirana.Tests.TestSupport;

namespace Kirana.Tests.Billing;

public class HeldBillServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly HeldBillService _sut;

    public HeldBillServiceTests()
    {
        _sut = new HeldBillService(_fixture.Context);
    }

    private async Task<Product> SeedProductAsync(string name = "Test Product")
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = name,
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 10,
            Mrp = 15,
            SellingPrice = 14,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task HoldAsync_CreatesHeldBillWithItems()
    {
        var product = await SeedProductAsync();

        var held = await _sut.HoldAsync(
            [new SaleLineInput { ProductId = product.Id, Quantity = 3, DiscountPercent = 5 }],
            billDiscountPercent: 0, customerId: null, cashierUserId: null, note: "Regular customer");

        Assert.Single(held.Items);
        Assert.Equal(3m, held.Items.Single().Quantity);
        Assert.Equal("Regular customer", held.Note);
    }

    [Fact]
    public async Task HoldAsync_Throws_WhenCartEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.HoldAsync([], 0, null, null, null));
    }

    [Fact]
    public async Task HoldAsync_Throws_WhenProductNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.HoldAsync([new SaleLineInput { ProductId = 999, Quantity = 1 }], 0, null, null, null));
    }

    [Fact]
    public async Task GetHeldBillsAsync_ReturnsAllHeldBillsOrderedByHeldTime()
    {
        var product = await SeedProductAsync();
        await _sut.HoldAsync([new SaleLineInput { ProductId = product.Id, Quantity = 1 }], 0, null, null, "First");
        await _sut.HoldAsync([new SaleLineInput { ProductId = product.Id, Quantity = 2 }], 0, null, null, "Second");

        var held = await _sut.GetHeldBillsAsync();

        Assert.Equal(2, held.Count);
        Assert.Equal("First", held[0].Note);
        Assert.Equal("Second", held[1].Note);
    }

    [Fact]
    public async Task ResumeAsync_ReturnsBillAndRemovesItFromHeldList()
    {
        var product = await SeedProductAsync();
        var held = await _sut.HoldAsync([new SaleLineInput { ProductId = product.Id, Quantity = 4 }], 0, null, null, null);

        var resumed = await _sut.ResumeAsync(held.Id);

        Assert.Single(resumed.Items);
        Assert.Equal(4m, resumed.Items.Single().Quantity);
        Assert.Empty(await _sut.GetHeldBillsAsync());
    }

    [Fact]
    public async Task ResumeAsync_Throws_WhenNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ResumeAsync(999));
    }

    public void Dispose() => _fixture.Dispose();
}
