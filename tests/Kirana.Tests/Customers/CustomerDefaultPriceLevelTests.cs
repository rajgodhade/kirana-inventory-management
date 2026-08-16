using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Products;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Customers;

/// <summary>
/// Phase 15B-4: a customer can carry a default price level.
///
/// <para>The distinction these tests exist to protect is that the customer default is a
/// <b>preference</b>, not a pricing authority. It seeds an empty bill's level and nothing more —
/// <see cref="SaleService"/> prices from <see cref="CompleteSaleRequest.PriceLevel"/> and never
/// looks at the customer at all. A cheaper default must not become a way to get cheaper prices.</para>
/// </summary>
public class CustomerDefaultPriceLevelTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly CustomerService _customers;
    private readonly SaleService _sales;
    private readonly int _ownerId;

    public CustomerDefaultPriceLevelTests()
    {
        _ownerId = _fixture.SeedOwnerAsync().GetAwaiter().GetResult().Id;

        // Phase 16A-2: cash transactions require an open register. These tests use cash as
        // fixture setup for something else, so the shop is simply open for business.
        _fixture.SeedOpenRegisterAsync().GetAwaiter().GetResult();
        var sequence = new EfSequenceGenerator(_fixture.Context);
        var audit = new EfAuditLogger(_fixture.Context);
        var permissions = new Kirana.Application.Authentication.PermissionEnforcer(_fixture.Context);

        _customers = new CustomerService(_fixture.Context, sequence, audit);
        _sales = new SaleService(_fixture.Context, sequence, audit, permissions);
    }

    private Task<Customer> CreateCustomerAsync(PriceLevel? level, string name = "ABC Wholesale Store") =>
        _customers.CreateAsync(new CreateCustomerRequest
        {
            Name = name,
            Phone = $"9{Random.Shared.Next(100000000, 999999999)}",
            DefaultPriceLevel = level,
            PerformedByUserId = _ownerId,
        });

    private async Task<Product> SeedProductAsync(decimal retail = 100m, decimal? wholesale = 90m)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = "Levelled Product",
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = 40m,
            Mrp = 300m,
            SellingPrice = retail,
            WholesalePrice = wholesale,
            IsActive = true,
        }.WithRetailPrice();

        if (wholesale is { } w)
        {
            product.WithWholesalePrice(w);
        }

        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 50m });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    // ---- Storage and NULL semantics ----

    [Fact]
    public async Task ACustomerCreatedWithoutAPreference_StoresNull()
    {
        var customer = await CreateCustomerAsync(null, "Walk-in Regular");

        var reloaded = await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id);
        Assert.Null(reloaded.DefaultPriceLevel);
    }

    [Theory]
    [InlineData("Retail")]
    [InlineData("Wholesale")]
    public async Task ACustomerCreatedWithAPreference_StoresIt(string level)
    {
        var expected = Enum.Parse<PriceLevel>(level);
        var customer = await CreateCustomerAsync(expected);

        var reloaded = await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id);
        Assert.Equal(expected, reloaded.DefaultPriceLevel);
    }

    [Fact]
    public async Task UpdatingACustomer_ChangesThePreference_AndCanClearItBackToNone()
    {
        var customer = await CreateCustomerAsync(PriceLevel.Wholesale);

        await _customers.UpdateAsync(customer.Id, new UpdateCustomerRequest
        {
            Name = customer.Name, Phone = customer.Phone,
            DefaultPriceLevel = PriceLevel.Retail, PerformedByUserId = _ownerId,
        });
        Assert.Equal(PriceLevel.Retail,
            (await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id)).DefaultPriceLevel);

        await _customers.UpdateAsync(customer.Id, new UpdateCustomerRequest
        {
            Name = customer.Name, Phone = customer.Phone,
            DefaultPriceLevel = null, PerformedByUserId = _ownerId,
        });
        Assert.Null(
            (await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == customer.Id)).DefaultPriceLevel);
    }

    /// <summary>Null and an explicit Retail both open at Retail, but they are stored differently —
    /// one is "never classified", the other is a decision someone made.</summary>
    [Fact]
    public async Task NullAndExplicitRetail_AreStoredDistinctly_EvenThoughBothOpenAtRetail()
    {
        var unset = await CreateCustomerAsync(null, "Unclassified");
        var retail = await CreateCustomerAsync(PriceLevel.Retail, "Explicitly Retail");

        var reloadedUnset = await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == unset.Id);
        var reloadedRetail = await _fixture.Context.Customers.AsNoTracking().FirstAsync(c => c.Id == retail.Id);

        Assert.Null(reloadedUnset.DefaultPriceLevel);
        Assert.Equal(PriceLevel.Retail, reloadedRetail.DefaultPriceLevel);

        // The rule the POS applies to both: "no preference" resolves to Retail.
        Assert.Equal(PriceLevel.Retail, reloadedUnset.DefaultPriceLevel ?? PriceLevel.Retail);
        Assert.Equal(PriceLevel.Retail, reloadedRetail.DefaultPriceLevel ?? PriceLevel.Retail);
    }

    // ---- The opening-level policy (BillPriceLevelPolicy) ----

    [Theory]
    [InlineData(null, "Retail")]          // no preference behaves as Retail
    [InlineData("Retail", "Retail")]
    [InlineData("Wholesale", "Wholesale")]
    public void ANewBillOpensAtTheCustomersLevel(string? preference, string expected)
    {
        var customerDefault = preference is null ? (PriceLevel?)null : Enum.Parse<PriceLevel>(preference);

        Assert.Equal(Enum.Parse<PriceLevel>(expected), BillPriceLevelPolicy.ForNewBill(customerDefault));
    }

    /// <summary>A walk-in (no customer at all) opens at Retail.</summary>
    [Fact]
    public void ABillWithNoCustomer_OpensAtRetail() =>
        Assert.Equal(PriceLevel.Retail, BillPriceLevelPolicy.ForNewBill(null));

    [Theory]
    [InlineData("Wholesale", "Wholesale")]
    [InlineData("Retail", "Retail")]
    [InlineData(null, "Retail")]
    public void SelectingACustomerOnAnEmptyBill_AppliesTheirLevel(string? preference, string expected)
    {
        var customerDefault = preference is null ? (PriceLevel?)null : Enum.Parse<PriceLevel>(preference);

        var result = BillPriceLevelPolicy.WhenCustomerChanges(
            currentBillLevel: PriceLevel.Retail, customerDefault, cartIsEmpty: true);

        Assert.Equal(Enum.Parse<PriceLevel>(expected), result);
    }

    /// <summary>
    /// The rule that protects a quoted price: once the cart has lines, changing the customer must
    /// not move what those lines cost. The cashier has already told someone a number.
    /// </summary>
    [Theory]
    [InlineData("Retail", "Wholesale")]     // retail bill, wholesale customer arrives
    [InlineData("Wholesale", "Retail")]     // wholesale bill, retail customer arrives
    [InlineData("Wholesale", null)]         // wholesale bill, customer removed
    public void ChangingTheCustomerOnAPopulatedBill_KeepsTheBillsLevel(string billLevel, string? preference)
    {
        var current = Enum.Parse<PriceLevel>(billLevel);
        var customerDefault = preference is null ? (PriceLevel?)null : Enum.Parse<PriceLevel>(preference);

        var result = BillPriceLevelPolicy.WhenCustomerChanges(current, customerDefault, cartIsEmpty: false);

        Assert.Equal(current, result);
    }

    /// <summary>Removing the customer from an EMPTY bill returns it to Retail.</summary>
    [Fact]
    public void RemovingTheCustomerFromAnEmptyBill_ReturnsToRetail() =>
        Assert.Equal(PriceLevel.Retail,
            BillPriceLevelPolicy.WhenCustomerChanges(PriceLevel.Wholesale, null, cartIsEmpty: true));

    /// <summary>An explicit selector choice is not undone by the policy: with lines on the bill the
    /// operator's level always wins, whatever the customer prefers.</summary>
    [Fact]
    public void AnExplicitLevelChoice_SurvivesACustomerWithTheOppositePreference()
    {
        // Operator deliberately chose Retail on a bill for a wholesale customer.
        var result = BillPriceLevelPolicy.WhenCustomerChanges(
            currentBillLevel: PriceLevel.Retail,
            customerDefault: PriceLevel.Wholesale,
            cartIsEmpty: false);

        Assert.Equal(PriceLevel.Retail, result);
    }

    // ---- The customer default is NOT a sale authority ----

    /// <summary>
    /// THE test for this phase. A wholesale-default customer on a bill submitted as Retail must be
    /// charged RETAIL: the level travels on the request, and a customer preference cannot reach
    /// past it into pricing.
    /// </summary>
    [Fact]
    public async Task AWholesaleCustomer_IsStillChargedRetail_WhenTheBillSaysRetail()
    {
        var customer = await CreateCustomerAsync(PriceLevel.Wholesale);
        var product = await SeedProductAsync(retail: 100m, wholesale: 90m);

        var sale = await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            PriceLevel = PriceLevel.Retail,
            CustomerId = customer.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 100m, AmountTendered = 100m }],
            CashierUserId = _ownerId,
        });

        var soldAt = (await _fixture.Context.SaleItems.AsNoTracking().FirstAsync(i => i.SaleId == sale.Id))
            .UnitPriceSnapshot;
        Assert.Equal(100m, soldAt);
        Assert.NotEqual(90m, soldAt);
    }

    /// <summary>...and the mirror: a retail-default customer billed as Wholesale pays wholesale.
    /// The request decides, not the customer record.</summary>
    [Fact]
    public async Task ARetailCustomer_IsChargedWholesale_WhenTheBillSaysWholesale()
    {
        var customer = await CreateCustomerAsync(PriceLevel.Retail, "Retail Regular");
        var product = await SeedProductAsync(retail: 100m, wholesale: 90m);

        var sale = await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            PriceLevel = PriceLevel.Wholesale,
            CustomerId = customer.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 90m, AmountTendered = 90m }],
            CashierUserId = _ownerId,
        });

        Assert.Equal(90m, (await _fixture.Context.SaleItems.AsNoTracking()
            .FirstAsync(i => i.SaleId == sale.Id)).UnitPriceSnapshot);
    }

    /// <summary>A customer whose default is Wholesale does not soften the missing-level rule.</summary>
    [Fact]
    public async Task AWholesaleCustomer_StillCannotBuyAProductWithNoWholesalePrice()
    {
        var customer = await CreateCustomerAsync(PriceLevel.Wholesale);
        var product = await SeedProductAsync(retail: 100m, wholesale: null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sales.CompleteSaleAsync(new CompleteSaleRequest
            {
                PriceLevel = PriceLevel.Wholesale,
                CustomerId = customer.Id,
                Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
                Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 100m, AmountTendered = 100m }],
                CashierUserId = _ownerId,
            }));

        Assert.Contains("wholesale", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());
    }

    // ---- Changing the preference is inert with respect to everything already recorded ----

    [Fact]
    public async Task ChangingACustomersPreference_LeavesHistoricalSalesAndPricesUntouched()
    {
        var customer = await CreateCustomerAsync(PriceLevel.Wholesale);
        var product = await SeedProductAsync(retail: 100m, wholesale: 90m);

        var sale = await _sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            PriceLevel = PriceLevel.Wholesale,
            CustomerId = customer.Id,
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 90m, AmountTendered = 90m }],
            CashierUserId = _ownerId,
        });

        var pricesBefore = await _fixture.Context.ProductPrices.AsNoTracking()
            .OrderBy(p => p.Id).Select(p => p.Price).ToListAsync();
        var stockBefore = (await _fixture.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand;

        await _customers.UpdateAsync(customer.Id, new UpdateCustomerRequest
        {
            Name = customer.Name, Phone = customer.Phone,
            DefaultPriceLevel = PriceLevel.Retail, PerformedByUserId = _ownerId,
        });

        Assert.Equal(90m, (await _fixture.Context.SaleItems.AsNoTracking()
            .FirstAsync(i => i.SaleId == sale.Id)).UnitPriceSnapshot);
        Assert.Equal(pricesBefore, await _fixture.Context.ProductPrices.AsNoTracking()
            .OrderBy(p => p.Id).Select(p => p.Price).ToListAsync());
        Assert.Equal(stockBefore, (await _fixture.Context.Inventories.AsNoTracking()
            .FirstAsync(i => i.ProductId == product.Id)).QuantityOnHand);
        Assert.Equal(1, await _fixture.Context.Sales.CountAsync());
    }

    /// <summary>A preference change is a customer edit, not a pricing event — it must not appear in
    /// the trail as one.</summary>
    [Fact]
    public async Task ChangingThePreference_WritesACustomerAudit_AndNoPricingAudit()
    {
        var customer = await CreateCustomerAsync(PriceLevel.Wholesale);

        await _customers.UpdateAsync(customer.Id, new UpdateCustomerRequest
        {
            Name = customer.Name, Phone = customer.Phone,
            DefaultPriceLevel = PriceLevel.Retail, PerformedByUserId = _ownerId,
        });

        var audit = await _fixture.Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "CustomerUpdated").OrderBy(a => a.Id).LastAsync();
        Assert.Contains("Wholesale", audit.PreviousValue!);
        Assert.Contains("Retail", audit.NewValue!);

        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "PriceChanged"));
        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "PriceModification"));
    }

    /// <summary>Re-saving the same preference is not a change and should not claim one.</summary>
    [Fact]
    public async Task ReSavingTheSamePreference_DoesNotClaimAPriceLevelChange()
    {
        var customer = await CreateCustomerAsync(PriceLevel.Wholesale);

        await _customers.UpdateAsync(customer.Id, new UpdateCustomerRequest
        {
            Name = customer.Name, Phone = customer.Phone,
            DefaultPriceLevel = PriceLevel.Wholesale, PerformedByUserId = _ownerId,
        });

        var audit = await _fixture.Context.AuditLogs.AsNoTracking()
            .Where(a => a.Action == "CustomerUpdated").OrderBy(a => a.Id).LastAsync();
        Assert.Null(audit.PreviousValue);
        Assert.DoesNotContain("default price level", audit.NewValue!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reading a customer never mutates anything — selecting one at the till must be free.</summary>
    [Fact]
    public async Task ReadingCustomers_CreatesNoSaleStockOrAudit()
    {
        await CreateCustomerAsync(PriceLevel.Wholesale);
        await SeedProductAsync();
        var auditsBefore = await _fixture.Context.AuditLogs.CountAsync();

        for (var i = 0; i < 3; i++)
        {
            await _customers.SearchAsync(new CustomerSearchQuery { SearchText = "ABC" });
        }

        Assert.Equal(auditsBefore, await _fixture.Context.AuditLogs.CountAsync());
        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());
        Assert.Equal(0, await _fixture.Context.StockMovements.CountAsync());
    }

    public void Dispose() => _fixture.Dispose();
}
