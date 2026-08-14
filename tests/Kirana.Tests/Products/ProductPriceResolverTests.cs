using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Products;

/// <summary>
/// Phase 15B-1: the read side of pricing. <see cref="IProductPriceResolver"/> answers "what does
/// this product cost under this context?" from the authoritative <see cref="ProductPrice"/> rows.
///
/// <para>The tests that matter most here are the two authoritative-source ones: they deliberately
/// desynchronise the legacy projection columns and prove the resolver still returns the
/// ProductPrice value. Without those, a resolver that quietly read Product.SellingPrice would pass
/// every other test in this file.</para>
/// </summary>
public class ProductPriceResolverTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly ProductService _products;
    private readonly ProductPricingService _pricing;
    private readonly ProductPriceResolver _sut;
    private readonly int _ownerId;

    public ProductPriceResolverTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        var sequence = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new PermissionEnforcer(_fixture.Context);
        var barcodes = new BarcodeService(_fixture.Context, sequence, audit, permissions);

        _pricing = new ProductPricingService(_fixture.Context, audit, permissions);
        _products = new ProductService(_fixture.Context, sequence, audit, barcodes, permissions, _pricing);
        _sut = new ProductPriceResolver(_fixture.Context);
    }

    private Task<Product> CreateAsync(decimal retail = 100m, decimal? wholesale = null, string name = "Resolver Product") =>
        _products.CreateAsync(new CreateProductRequest
        {
            Name = name,
            Sku = $"SKU-{Guid.NewGuid():N}"[..10],
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 60m,
            Mrp = 200m,
            SellingPrice = retail,
            WholesalePrice = wholesale,
            PerformedByUserId = _ownerId,
        });

    /// <summary>Desynchronises a projection column behind the pricing service's back. Only a test
    /// may do this — it is exactly the state the service exists to prevent — and it is the only way
    /// to prove which store the resolver actually reads.</summary>
    private async Task DesyncProjectionAsync(int productId, decimal? selling = null, decimal? wholesale = null)
    {
        var tracked = await _fixture.Context.Products.FirstAsync(p => p.Id == productId);
        if (selling is { } s) tracked.SellingPrice = s;
        if (wholesale is { } w) tracked.WholesalePrice = w;
        await _fixture.Context.SaveChangesAsync();
    }

    // ---- §23 Retail ----

    [Fact]
    public async Task ResolvesRetail_FromTheConfiguredPrice()
    {
        var product = await CreateAsync(retail: 100m);

        var result = await _sut.ResolveAsync(product.Id, PricingContext.Retail);

        Assert.True(result.IsResolved);
        Assert.Equal(100m, result.UnitPrice);
        Assert.Equal(PriceLevel.Retail, result.Level);
        Assert.Equal(PriceSource.ConfiguredPrice, result.Source);
        Assert.Equal(product.Id, result.ProductId);
        Assert.Null(result.UnavailableReason);
    }

    /// <summary>Money keeps its exact decimal value — no rounding on the way out.</summary>
    [Theory]
    [InlineData("57.50")]
    [InlineData("0.01")]
    [InlineData("1234.56")]
    [InlineData("0")]
    public async Task ResolvesRetail_PreservingTheExactDecimal(string raw)
    {
        var expected = decimal.Parse(raw);
        var product = await CreateAsync(retail: expected);

        var result = await _sut.ResolveAsync(product.Id, PricingContext.Retail);

        Assert.True(result.IsResolved);
        Assert.Equal(expected, result.UnitPrice);
    }

    // ---- §24 Wholesale ----

    [Fact]
    public async Task ResolvesEachLevel_FromItsOwnConfiguredPrice()
    {
        var product = await CreateAsync(retail: 100m, wholesale: 90m);

        var retail = await _sut.ResolveAsync(product.Id, PricingContext.Retail);
        var wholesale = await _sut.ResolveAsync(product.Id, PricingContext.Wholesale);

        Assert.Equal(100m, retail.UnitPrice);
        Assert.Equal(90m, wholesale.UnitPrice);
        Assert.Equal(PriceLevel.Retail, retail.Level);
        Assert.Equal(PriceLevel.Wholesale, wholesale.Level);
    }

    /// <summary>An explicitly configured zero is a price, not "unset" — the NULL-vs-zero distinction
    /// Phase 15A preserves has to survive the read path too.</summary>
    [Fact]
    public async Task ResolvesAZeroWholesale_AsAPriceRatherThanUnavailable()
    {
        var product = await CreateAsync(retail: 100m, wholesale: 0m);

        var result = await _sut.ResolveAsync(product.Id, PricingContext.Wholesale);

        Assert.True(result.IsResolved);
        Assert.Equal(0m, result.UnitPrice);
    }

    // ---- §25 Missing wholesale must NOT fall back ----

    [Fact]
    public async Task WholesaleOnAProductWithoutIt_IsUnavailable_AndNeverFallsBackToRetail()
    {
        var product = await CreateAsync(retail: 100m, wholesale: null);

        var result = await _sut.ResolveAsync(product.Id, PricingContext.Wholesale);

        Assert.False(result.IsResolved);
        Assert.Equal(PriceUnavailableReason.LevelNotConfigured, result.UnavailableReason);
        Assert.Null(result.UnitPrice);
        Assert.NotEqual(100m, result.UnitPrice);   // the retail price must not leak through
        Assert.Equal(PriceLevel.Wholesale, result.Level);

        // ...while retail on the same product still resolves, so this is not a broken product.
        Assert.Equal(100m, (await _sut.ResolveAsync(product.Id, PricingContext.Retail)).UnitPrice);
    }

    // ---- §26 Inactive price ----

    [Fact]
    public async Task AWithdrawnLevel_IsUnavailable_NotResurrected()
    {
        var product = await CreateAsync(retail: 100m, wholesale: 90m);
        await _pricing.RemovePriceAsync(product.Id, PriceLevel.Wholesale, _ownerId);

        var result = await _sut.ResolveAsync(product.Id, PricingContext.Wholesale);

        Assert.False(result.IsResolved);
        Assert.Equal(PriceUnavailableReason.LevelNotConfigured, result.UnavailableReason);

        // The deactivated row is still on disk — proving the resolver filtered it rather than the
        // row simply being gone.
        Assert.True(await _fixture.Context.ProductPrices
            .AnyAsync(p => p.ProductId == product.Id && p.Level == PriceLevel.Wholesale && !p.IsActive));
    }

    // ---- §27 Inactive product ----

    /// <summary>
    /// Matches how POS reads already treat discontinued stock: BarcodeLookupService filters
    /// <c>b.Product.IsActive</c> so a retired product simply does not resolve. SaleService keeps its
    /// own "inactive and cannot be sold" throw for anything that reaches a bill; the resolver does
    /// not duplicate that rule, it just reports there is no current price.
    /// </summary>
    [Fact]
    public async Task AnInactiveProduct_HasNoResolvablePrice()
    {
        var product = await CreateAsync(retail: 100m, wholesale: 90m);
        await _products.SetActiveAsync(product.Id, false, _ownerId);

        foreach (var context in new[] { PricingContext.Retail, PricingContext.Wholesale })
        {
            var result = await _sut.ResolveAsync(product.Id, context);
            Assert.False(result.IsResolved);
            Assert.Equal(PriceUnavailableReason.ProductInactive, result.UnavailableReason);
            Assert.Null(result.UnitPrice);
        }
    }

    // ---- §28 Missing product ----

    [Fact]
    public async Task AnUnknownProduct_Throws_RatherThanReturningZeroOrUnavailable()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ResolveAsync(999_999, PricingContext.Retail));

        Assert.Contains("999999", error.Message.Replace(",", string.Empty));
        Assert.Contains("not found", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- §29 No side effects ----

    [Fact]
    public async Task ResolvingRepeatedly_ChangesNothing()
    {
        var product = await CreateAsync(retail: 100m, wholesale: 90m);
        var auditsBefore = await _fixture.Context.AuditLogs.CountAsync();
        var pricesBefore = await _fixture.Context.ProductPrices.CountAsync();
        var before = await _fixture.Context.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);

        for (var i = 0; i < 5; i++)
        {
            await _sut.ResolveAsync(product.Id, PricingContext.Retail);
            await _sut.ResolveAsync(product.Id, PricingContext.Wholesale);
        }

        Assert.Equal(auditsBefore, await _fixture.Context.AuditLogs.CountAsync());
        Assert.Equal(pricesBefore, await _fixture.Context.ProductPrices.CountAsync());
        Assert.Equal(0, await _fixture.Context.StockMovements.CountAsync());
        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());

        var after = await _fixture.Context.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        Assert.Equal(before.SellingPrice, after.SellingPrice);
        Assert.Equal(before.WholesalePrice, after.WholesalePrice);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);

        // Nothing was left tracked and dirty either.
        Assert.False(_fixture.Context.ChangeTracker.HasChanges());
    }

    // ---- §30/§31 ProductPrice is the authoritative source ----

    /// <summary>
    /// THE test for this phase. With the projection deliberately disagreeing, only a resolver that
    /// reads ProductPrice can return 100. One that reads Product.SellingPrice returns 95.
    /// </summary>
    [Fact]
    public async Task ResolvesRetail_FromProductPrice_NotTheSellingPriceProjection()
    {
        var product = await CreateAsync(retail: 100m);
        await DesyncProjectionAsync(product.Id, selling: 95m);

        var result = await _sut.ResolveAsync(product.Id, PricingContext.Retail);

        Assert.Equal(100m, result.UnitPrice);
        Assert.NotEqual(95m, result.UnitPrice);
        // ...and the divergence really is present, so the assertion above means something.
        Assert.Equal(95m, (await _fixture.Context.Products.AsNoTracking()
            .FirstAsync(p => p.Id == product.Id)).SellingPrice);
    }

    [Fact]
    public async Task ResolvesWholesale_FromProductPrice_NotTheWholesalePriceProjection()
    {
        var product = await CreateAsync(retail: 100m, wholesale: 90m);
        await DesyncProjectionAsync(product.Id, wholesale: 95m);

        var result = await _sut.ResolveAsync(product.Id, PricingContext.Wholesale);

        Assert.Equal(90m, result.UnitPrice);
        Assert.NotEqual(95m, result.UnitPrice);
        Assert.Equal(95m, (await _fixture.Context.Products.AsNoTracking()
            .FirstAsync(p => p.Id == product.Id)).WholesalePrice);
    }

    // ---- §32/§33 No cross-level or cross-product bleed ----

    [Fact]
    public async Task DoesNotResolveAnotherProductsPrice()
    {
        var a = await CreateAsync(retail: 100m, wholesale: 90m, name: "Product A");
        var b = await CreateAsync(retail: 200m, wholesale: 190m, name: "Product B");

        Assert.Equal(100m, (await _sut.ResolveAsync(a.Id, PricingContext.Retail)).UnitPrice);
        Assert.Equal(200m, (await _sut.ResolveAsync(b.Id, PricingContext.Retail)).UnitPrice);
        Assert.Equal(90m, (await _sut.ResolveAsync(a.Id, PricingContext.Wholesale)).UnitPrice);
        Assert.Equal(190m, (await _sut.ResolveAsync(b.Id, PricingContext.Wholesale)).UnitPrice);
    }

    /// <summary>One product's withdrawn wholesale must not make another product's active wholesale
    /// unavailable, and vice versa.</summary>
    [Fact]
    public async Task OneProductsWithdrawnLevel_DoesNotAffectAnother()
    {
        var withdrawn = await CreateAsync(retail: 100m, wholesale: 90m, name: "Withdrawn");
        var kept = await CreateAsync(retail: 100m, wholesale: 80m, name: "Kept");
        await _pricing.RemovePriceAsync(withdrawn.Id, PriceLevel.Wholesale, _ownerId);

        Assert.False((await _sut.ResolveAsync(withdrawn.Id, PricingContext.Wholesale)).IsResolved);
        Assert.Equal(80m, (await _sut.ResolveAsync(kept.Id, PricingContext.Wholesale)).UnitPrice);
    }

    // ---- §34 Fresh reads ----

    /// <summary>
    /// A price changed and committed by another context must be what the resolver returns. The
    /// resolver projects to an anonymous type with AsNoTracking, so it cannot hand back a stale
    /// tracked entity — the identity-map trap that produced the Phase 13C stock bug.
    /// </summary>
    [Fact]
    public async Task ReadsThePriceCommittedByAnotherContext()
    {
        var product = await CreateAsync(retail: 100m);
        Assert.Equal(100m, (await _sut.ResolveAsync(product.Id, PricingContext.Retail)).UnitPrice);

        // A separate context on the same in-memory database, as a second process would be.
        await using (var other = NewContext())
        {
            var otherPricing = new ProductPricingService(
                other, new EfAuditLogger(other), new PermissionEnforcer(other));
            await otherPricing.SetPriceAsync(product.Id, PriceLevel.Retail, 111m, _ownerId);
        }

        var result = await _sut.ResolveAsync(product.Id, PricingContext.Retail);

        Assert.Equal(111m, result.UnitPrice);
    }

    // ---- §11 Defensive: never arbitrate between duplicate active rows ----

    /// <summary>
    /// Phase 15A's filtered unique index makes two active rows for one (product, level) impossible
    /// through the application, so this test drops that index to manufacture corrupt data. It does
    /// not weaken production — it proves that IF such data were ever encountered, the resolver
    /// refuses rather than billing whichever row the query happened to return first.
    /// </summary>
    [Fact]
    public async Task RefusesToChooseBetweenDuplicateActivePrices()
    {
        var product = await CreateAsync(retail: 100m);

        var connection = (SqliteConnection)_fixture.Context.Database.GetDbConnection();
        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText = "DROP INDEX IX_ProductPrices_ProductId_Level_Active";
            await drop.ExecuteNonQueryAsync();
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO ProductPrices (ProductId, Level, Price, IsActive, CreatedAtUtc)
                VALUES ($id, 'Retail', 999, 1, CURRENT_TIMESTAMP);
                """;
            insert.Parameters.Add(new SqliteParameter("$id", product.Id));
            await insert.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.ResolveAsync(product.Id, PricingContext.Retail));

        Assert.Contains("active", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- §35 Authorization ----

    /// <summary>
    /// Reading a price is not a privileged operation: a cashier prices every line they scan. The
    /// resolver takes no user at all, which is the point — proven here by showing the same cashier
    /// who is refused a price CHANGE can still resolve one.
    /// </summary>
    [Fact]
    public async Task ACashierWhoCannotEditPrices_CanStillResolveThem()
    {
        var product = await CreateAsync(retail: 100m, wholesale: 90m);
        var cashierId = (await _fixture.SeedCashierAsync()).Id;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _pricing.SetPriceAsync(product.Id, PriceLevel.Retail, 200m, cashierId));

        Assert.Equal(100m, (await _sut.ResolveAsync(product.Id, PricingContext.Retail)).UnitPrice);
        Assert.Equal(90m, (await _sut.ResolveAsync(product.Id, PricingContext.Wholesale)).UnitPrice);
    }

    private KiranaDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KiranaDbContext>()
            .UseSqlite((SqliteConnection)_fixture.Context.Database.GetDbConnection())
            .Options);

    public void Dispose() => _fixture.Dispose();
}
