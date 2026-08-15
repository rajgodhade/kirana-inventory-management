using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Printing;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Printing;

public class InvoicePrintServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();
    private readonly EfSequenceGenerator _sequenceGenerator;
    private readonly EfAuditLogger _auditLogger;
    private readonly PermissionEnforcer _permissionEnforcer;
    private readonly SaleService _saleService;
    private readonly InvoicePrintService _sut;

    public InvoicePrintServiceTests()
    {
        _sequenceGenerator = new EfSequenceGenerator(_fixture.Context);
        _auditLogger = new EfAuditLogger(_fixture.Context);
        _permissionEnforcer = new PermissionEnforcer(_fixture.Context);
        _saleService = new SaleService(_fixture.Context, _sequenceGenerator, _auditLogger, _permissionEnforcer);
        _sut = new InvoicePrintService(_fixture.Context, _saleService, new InvoiceDocumentBuilder(), _auditLogger, _permissionEnforcer);
    }

    private async Task SeedStoreAsync() =>
        await AddAndSaveAsync(new Store { Name = "Test Store", OwnerName = "Owner", SetupCompleted = true });

    private async Task<Product> SeedProductAsync(string name = "Tata Salt 1kg", decimal price = 25, decimal stock = 100)
    {
        var product = new Product
        {
            ProductCode = $"PRD-{Guid.NewGuid():N}"[..12],
            Name = name,
            Unit = UnitOfMeasure.Piece,
            PurchasePrice = price * 0.8m,
            Mrp = price + 5,
            SellingPrice = price,
            IsActive = true,
        }.WithRetailPrice();
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = stock });
        await _fixture.Context.SaveChangesAsync();
        return product;
    }

    private async Task AddAndSaveAsync<T>(T entity) where T : class
    {
        _fixture.Context.Set<T>().Add(entity);
        await _fixture.Context.SaveChangesAsync();
    }

    private async Task<Sale> CompleteCashSaleAsync(int productId, decimal quantity, decimal amount) =>
        await _saleService.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = productId, Quantity = quantity }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = amount, AmountTendered = amount }],
        });

    [Fact]
    public async Task GetInvoiceDocumentAsync_ReturnsDocument_ForCompletedSale()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(price: 50);
        var sale = await CompleteCashSaleAsync(product.Id, 2, 100);

        var document = await _sut.GetInvoiceDocumentAsync(sale.Id);

        Assert.Equal(sale.InvoiceNumber, document.InvoiceNumber);
        Assert.Equal(100m, document.GrandTotal);
        Assert.Single(document.Lines);
    }

    [Fact]
    public async Task GetInvoiceDocumentAsync_Throws_WhenSaleNotFound()
    {
        await SeedStoreAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetInvoiceDocumentAsync(999));
    }

    [Fact]
    public async Task GetInvoiceDocumentAsync_Throws_WhenStoreNotConfigured()
    {
        var product = await SeedProductAsync(price: 10);
        var sale = await CompleteCashSaleAsync(product.Id, 1, 10);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetInvoiceDocumentAsync(sale.Id));
    }

    [Fact]
    public async Task GetInvoiceDocumentByInvoiceNumberAsync_FindsSaleByInvoiceNumber()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(price: 20);
        var sale = await CompleteCashSaleAsync(product.Id, 1, 20);

        var document = await _sut.GetInvoiceDocumentByInvoiceNumberAsync(sale.InvoiceNumber);

        Assert.Equal(sale.Id, document.SaleId);
    }

    [Fact]
    public async Task GetInvoiceDocumentByInvoiceNumberAsync_Throws_WhenNotFound()
    {
        await SeedStoreAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetInvoiceDocumentByInvoiceNumberAsync("INV-2026-999999"));
    }

    [Fact]
    public async Task GetInvoiceDocumentAsync_UsesSnapshot_EvenAfterProductEditedLater()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(name: "Original Name", price: 30);
        var sale = await CompleteCashSaleAsync(product.Id, 1, 30);

        product.Name = "Renamed Later";
        product.SellingPrice = 999;
        await _fixture.Context.SaveChangesAsync();

        var document = await _sut.GetInvoiceDocumentAsync(sale.Id);

        var line = Assert.Single(document.Lines);
        Assert.Equal("Original Name", line.ProductName);
        Assert.Equal(30m, line.UnitPrice);
    }

    [Fact]
    public async Task LogPrintAsync_WritesInvoicePrinted_ForInitialPrint()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(price: 10);
        var sale = await CompleteCashSaleAsync(product.Id, 1, 10);

        await _sut.LogPrintAsync(sale.Id, userId: null, isReprint: false);

        var entry = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "InvoicePrinted");
        Assert.Equal(sale.Id.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task LogPrintAsync_WritesInvoiceReprinted_ForReprint()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var product = await SeedProductAsync(price: 10);
        var sale = await CompleteCashSaleAsync(product.Id, 1, 10);

        await _sut.LogPrintAsync(sale.Id, owner.Id, isReprint: true);

        var entry = await _fixture.Context.AuditLogs.SingleAsync(a => a.Action == "InvoiceReprinted");
        Assert.Equal(sale.Id.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task LogPrintAsync_Throws_WhenReprintingWithoutPermission()
    {
        await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        var product = await SeedProductAsync(price: 10);
        var sale = await CompleteCashSaleAsync(product.Id, 1, 10);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.LogPrintAsync(sale.Id, cashier.Id, isReprint: true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.LogPrintAsync(sale.Id, userId: null, isReprint: true));

        Assert.False(await _fixture.Context.AuditLogs.AnyAsync(a => a.Action == "InvoiceReprinted"));
    }

    [Fact]
    public async Task RetryingPrintAfterFailure_NeverCreatesAnotherSaleOrPaymentOrStockMovement()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(price: 10, stock: 50);
        var sale = await CompleteCashSaleAsync(product.Id, 1, 10);

        // Simulate: preview/print attempted, "failed", cashier retries a few times.
        await _sut.GetInvoiceDocumentAsync(sale.Id);
        await _sut.LogPrintAsync(sale.Id, userId: null, isReprint: false);
        await _sut.GetInvoiceDocumentAsync(sale.Id);
        await _sut.LogPrintAsync(sale.Id, userId: null, isReprint: false);

        Assert.Equal(1, await _fixture.Context.Sales.CountAsync());
        Assert.Equal(1, await _fixture.Context.Payments.CountAsync());
        Assert.Equal(1, await _fixture.Context.StockMovements.CountAsync());
        Assert.Equal(2, await _fixture.Context.AuditLogs.CountAsync(a => a.Action == "InvoicePrinted"));

        var inventory = await _fixture.Context.Inventories.SingleAsync(i => i.ProductId == product.Id);
        Assert.Equal(49m, inventory.QuantityOnHand);
    }

    public void Dispose() => _fixture.Dispose();
}
