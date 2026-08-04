using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Setup;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Infrastructure.Security;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Billing;

public class SaleServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly BCryptPasswordHasher _hasher = new();
    private readonly EfSequenceGenerator _sequenceGenerator;
    private readonly EfAuditLogger _auditLogger;
    private readonly SaleService _sut;

    public SaleServiceTests()
    {
        _sequenceGenerator = new EfSequenceGenerator(_fixture.Context);
        _auditLogger = new EfAuditLogger(_fixture.Context);
        _sut = new SaleService(_fixture.Context, _sequenceGenerator, _auditLogger, new PermissionEnforcer(_fixture.Context));
    }

    private async Task<Product> SeedProductAsync(
        string name = "Tata Salt 1kg", decimal price = 25, decimal stock = 100,
        UnitOfMeasure unit = UnitOfMeasure.Piece, decimal? gstRate = null, bool isTaxInclusive = false)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = name,
            Unit = unit,
            PurchasePrice = price * 0.8m,
            Mrp = price + 5,
            SellingPrice = price,
            GstRatePercent = gstRate,
            IsTaxInclusive = isTaxInclusive,
            IsActive = true,
        };
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private async Task EnableStoreGstAsync(bool enabled = true)
    {
        _fixture.Context.Stores.Add(new Store { Name = "Test Store", OwnerName = "Owner", IsGstEnabled = enabled, SetupCompleted = true });
        await _fixture.Context.SaveChangesAsync();
    }

    private async Task<User> SeedAuthorizedUserAsync()
    {
        var setup = new FirstTimeSetupService(_fixture.Context, _hasher);
        await setup.CompleteSetupAsync(new CompleteSetupRequest
        {
            StoreName = "Sharma Kirana Store",
            OwnerName = "Ramesh Sharma",
            AdminUsername = "admin",
            AdminFullName = "Ramesh Sharma",
            AdminPassword = "S3cure!Pass",
        });

        return await _fixture.Context.Users.Include(u => u.Role).SingleAsync();
    }

    private static CompleteSaleRequest CashRequest(int productId, decimal quantity, decimal amount) => new()
    {
        Lines = [new SaleLineInput { ProductId = productId, Quantity = quantity }],
        Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = amount, AmountTendered = amount }],
    };

    [Fact]
    public async Task CompleteSaleAsync_CreatesSaleWithCorrectTotals()
    {
        var product = await SeedProductAsync(price: 50);

        var sale = await _sut.CompleteSaleAsync(CashRequest(product.Id, 2, 100));

        Assert.Equal(100m, sale.SubTotal);
        Assert.Equal(100m, sale.GrandTotal);
        Assert.Single(sale.Items);
        Assert.Equal(SaleStatus.Completed, sale.Status);
    }

    [Fact]
    public async Task CompleteSaleAsync_GeneratesInvoiceNumberInExpectedFormat()
    {
        var product = await SeedProductAsync(price: 10);

        var sale = await _sut.CompleteSaleAsync(CashRequest(product.Id, 1, 10));

        Assert.Matches(@"^INV-\d{4}-\d{6}$", sale.InvoiceNumber);
    }

    [Fact]
    public async Task CompleteSaleAsync_GeneratesSequentialInvoiceNumbers()
    {
        var product = await SeedProductAsync(price: 10, stock: 100);

        var first = await _sut.CompleteSaleAsync(CashRequest(product.Id, 1, 10));
        var second = await _sut.CompleteSaleAsync(CashRequest(product.Id, 1, 10));

        var year = DateTime.UtcNow.Year;
        Assert.Equal($"INV-{year}-000001", first.InvoiceNumber);
        Assert.Equal($"INV-{year}-000002", second.InvoiceNumber);
    }

    [Fact]
    public async Task CompleteSaleAsync_DeductsStockAndWritesStockMovement()
    {
        var product = await SeedProductAsync(price: 10, stock: 50);

        await _sut.CompleteSaleAsync(CashRequest(product.Id, 5, 50));

        var inventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id);
        Assert.Equal(45m, inventory.QuantityOnHand);

        var movement = await _fixture.Context.StockMovements.SingleAsync(m => m.ProductId == product.Id);
        Assert.Equal(StockMovementType.Sale, movement.MovementType);
        Assert.Equal(-5m, movement.QuantityChange);
        Assert.Equal(50m, movement.PreviousQuantity);
        Assert.Equal(45m, movement.NewQuantity);
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenInsufficientStock()
    {
        var product = await SeedProductAsync(stock: 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteSaleAsync(CashRequest(product.Id, 5, 125)));
    }

    [Fact]
    public async Task CompleteSaleAsync_RollsBackEverything_WhenStockValidationFails()
    {
        var goodProduct = await SeedProductAsync(name: "Good", price: 10, stock: 100);
        var shortProduct = await SeedProductAsync(name: "Short", price: 10, stock: 1);

        var request = new CompleteSaleRequest
        {
            Lines =
            [
                new SaleLineInput { ProductId = goodProduct.Id, Quantity = 2 },
                new SaleLineInput { ProductId = shortProduct.Id, Quantity = 5 },
            ],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 70, AmountTendered = 70 }],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteSaleAsync(request));

        Assert.Equal(0, await _fixture.Context.Sales.CountAsync());
        Assert.Equal(0, await _fixture.Context.StockMovements.CountAsync());
        var goodInventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == goodProduct.Id);
        Assert.Equal(100m, goodInventory.QuantityOnHand);
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenCartEmpty()
    {
        var request = new CompleteSaleRequest
        {
            Lines = [],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 10 }],
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CompleteSaleAsync(request));
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenNoPayments()
    {
        var product = await SeedProductAsync();

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            Payments = [],
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CompleteSaleAsync(request));
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenPaymentTotalDoesNotMatchGrandTotal()
    {
        var product = await SeedProductAsync(price: 50);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteSaleAsync(CashRequest(product.Id, 2, 50)));
    }

    [Fact]
    public async Task CompleteSaleAsync_SplitPayment_CreatesMultiplePaymentRows()
    {
        var product = await SeedProductAsync(price: 100);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            Payments =
            [
                new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 60, AmountTendered = 60 },
                new SalePaymentInput { Method = PaymentMethod.Upi, Amount = 40, ReferenceNumber = "UPI123" },
            ],
        };

        var sale = await _sut.CompleteSaleAsync(request);

        Assert.Equal(2, sale.Payments.Count);
        Assert.Equal(100m, sale.Payments.Sum(p => p.Amount));
    }

    [Fact]
    public async Task CompleteSaleAsync_CashPayment_ComputesChangeGiven()
    {
        var product = await SeedProductAsync(price: 45);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 45, AmountTendered = 100 }],
        };

        var sale = await _sut.CompleteSaleAsync(request);

        var payment = sale.Payments.Single();
        Assert.Equal(100m, payment.AmountTendered);
        Assert.Equal(55m, payment.ChangeGiven);
    }

    [Fact]
    public async Task CompleteSaleAsync_CustomerCreditPayment_CreatesCreditAndUpdatesBalance()
    {
        var product = await SeedProductAsync(price: 200);
        var customer = new Customer { Name = "Regular Customer", IsActive = true };
        _fixture.Context.Customers.Add(customer);
        await _fixture.Context.SaveChangesAsync();

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            CustomerId = customer.Id,
            Payments = [new SalePaymentInput { Method = PaymentMethod.CustomerCredit, Amount = 200 }],
        };

        await _sut.CompleteSaleAsync(request);

        var credit = await _fixture.Context.CustomerCredits.SingleAsync();
        Assert.Equal(200m, credit.Amount);
        Assert.Equal(200m, credit.RemainingAmount);

        var updatedCustomer = await _fixture.Context.Customers.SingleAsync(c => c.Id == customer.Id);
        Assert.Equal(200m, updatedCustomer.CreditBalance);
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenCreditPaymentWithoutCustomer()
    {
        var product = await SeedProductAsync(price: 200);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.CustomerCredit, Amount = 200 }],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteSaleAsync(request));
    }

    [Fact]
    public async Task CompleteSaleAsync_StoresHistoricalSnapshot_ThatSurvivesLaterProductEdits()
    {
        var product = await SeedProductAsync(name: "Original Name", price: 30);

        var sale = await _sut.CompleteSaleAsync(CashRequest(product.Id, 1, 30));
        var saleId = sale.Id;

        product.Name = "Renamed Product";
        product.SellingPrice = 999;
        await _fixture.Context.SaveChangesAsync();

        var reloaded = await _sut.GetByIdAsync(saleId);
        var item = reloaded!.Items.Single();

        Assert.Equal("Original Name", item.ProductNameSnapshot);
        Assert.Equal(30m, item.UnitPriceSnapshot);
    }

    [Fact]
    public async Task CompleteSaleAsync_RejectsFractionalQuantity_ForWholeUnitProduct()
    {
        var product = await SeedProductAsync(unit: UnitOfMeasure.Piece, price: 10);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1.5m }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 15, AmountTendered = 15 }],
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CompleteSaleAsync(request));
    }

    [Fact]
    public async Task CompleteSaleAsync_AllowsFractionalQuantity_ForWeightBasedProduct()
    {
        var product = await SeedProductAsync(unit: UnitOfMeasure.Kilogram, price: 40, stock: 10);

        var sale = await _sut.CompleteSaleAsync(CashRequest(product.Id, 1.25m, 50));

        Assert.Equal(1.25m, sale.Items.Single().Quantity);
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenLargeDiscountNotAuthorized()
    {
        var product = await SeedProductAsync(price: 100);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1, DiscountPercent = 20 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 80, AmountTendered = 80 }],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteSaleAsync(request));
    }

    [Fact]
    public async Task CompleteSaleAsync_Succeeds_WhenLargeDiscountAuthorizedByManager()
    {
        var manager = await SeedAuthorizedUserAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1, DiscountPercent = 20 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 80, AmountTendered = 80 }],
            DiscountAuthorizedByUserId = manager.Id,
        };

        var sale = await _sut.CompleteSaleAsync(request);

        Assert.Equal(80m, sale.GrandTotal);
        Assert.Equal(manager.Id, sale.DiscountAuthorizedByUserId);
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenAuthorizingUserLacksPermission()
    {
        var product = await SeedProductAsync(price: 100);

        // A user id that exists but was never granted BillingApproveLargeDiscount: none seeded,
        // so this id simply doesn't resolve to any authorized user — service must reject it.
        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1, DiscountPercent = 50 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 50, AmountTendered = 50 }],
            DiscountAuthorizedByUserId = 999,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteSaleAsync(request));
    }

    [Fact]
    public async Task CompleteSaleAsync_UsesOverriddenPrice_WhenAuthorized()
    {
        var manager = await SeedAuthorizedUserAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1, UnitPriceOverride = 70 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 70, AmountTendered = 70 }],
            PriceOverrideAuthorizedByUserId = manager.Id,
        };

        var sale = await _sut.CompleteSaleAsync(request);

        Assert.Equal(70m, sale.GrandTotal);
        Assert.Equal(70m, sale.Items.Single().UnitPriceSnapshot);
        Assert.Equal(manager.Id, sale.PriceOverrideAuthorizedByUserId);
    }

    [Fact]
    public async Task CompleteSaleAsync_NeverMutatesTheProductsOwnSellingPrice_WhenOverridden()
    {
        var manager = await SeedAuthorizedUserAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1, UnitPriceOverride = 70 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 70, AmountTendered = 70 }],
            PriceOverrideAuthorizedByUserId = manager.Id,
        };

        await _sut.CompleteSaleAsync(request);

        var reloaded = await _fixture.Context.Products.SingleAsync(p => p.Id == product.Id);
        Assert.Equal(100m, reloaded.SellingPrice);
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenPriceOverrideNotAuthorized()
    {
        var product = await SeedProductAsync(price: 100);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1, UnitPriceOverride = 70 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 70, AmountTendered = 70 }],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteSaleAsync(request));
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenPriceOverrideAuthorizingUserLacksPermission()
    {
        var product = await SeedProductAsync(price: 100);

        // Same "id exists but was never granted the permission" shape as the discount-authorization
        // rejection test above — no user with id 999 was ever seeded.
        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1, UnitPriceOverride = 70 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 70, AmountTendered = 70 }],
            PriceOverrideAuthorizedByUserId = 999,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.CompleteSaleAsync(request));
    }

    [Fact]
    public async Task CompleteSaleAsync_Throws_WhenOverriddenPriceIsNegative()
    {
        var manager = await SeedAuthorizedUserAsync();
        var product = await SeedProductAsync(price: 100);

        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1, UnitPriceOverride = -1 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 0, AmountTendered = 0 }],
            PriceOverrideAuthorizedByUserId = manager.Id,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CompleteSaleAsync(request));
    }

    [Fact]
    public async Task CompleteSaleAsync_DoesNotRequireAuthorization_WhenOverridePriceMatchesCurrentPrice()
    {
        var product = await SeedProductAsync(price: 100);

        // UnitPriceOverride is set but happens to equal the product's current price — this is not
        // really an override in effect, so it must not demand manager authorization.
        var request = new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1, UnitPriceOverride = 100 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 100, AmountTendered = 100 }],
        };

        var sale = await _sut.CompleteSaleAsync(request);

        Assert.Equal(100m, sale.GrandTotal);
        Assert.Null(sale.PriceOverrideAuthorizedByUserId);
    }

    [Fact]
    public async Task CompleteSaleAsync_AppliesGst_WhenStoreGstEnabled()
    {
        await EnableStoreGstAsync(true);
        var product = await SeedProductAsync(price: 100, gstRate: 5, isTaxInclusive: false);

        var sale = await _sut.CompleteSaleAsync(CashRequest(product.Id, 1, 105));

        Assert.Equal(5m, sale.TaxTotal);
    }

    [Fact]
    public async Task CompleteSaleAsync_IgnoresGst_WhenStoreGstDisabled()
    {
        await EnableStoreGstAsync(false);
        var product = await SeedProductAsync(price: 100, gstRate: 18, isTaxInclusive: false);

        var sale = await _sut.CompleteSaleAsync(CashRequest(product.Id, 1, 100));

        Assert.Equal(0m, sale.TaxTotal);
    }

    public void Dispose() => _fixture.Dispose();
}
