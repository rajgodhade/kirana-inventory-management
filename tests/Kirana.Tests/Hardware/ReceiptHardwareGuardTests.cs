using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Hardware;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Hardware;

public sealed class ReceiptHardwareGuardTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();

    [Fact]
    public async Task UnavailablePrinter_ReturnsWarningInsteadOfThrowing()
    {
        var sut = new ReceiptHardwareGuard(new FakeSettings(), new FakePrinter(available: false));
        var result = await sut.CheckAvailabilityAsync();
        Assert.False(result.Succeeded);
        Assert.Contains("unavailable", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrinterApiFault_IsContained()
    {
        var sut = new ReceiptHardwareGuard(new FakeSettings(), new ThrowingPrinter());
        var result = await sut.CheckAvailabilityAsync();
        Assert.False(result.Succeeded);
        Assert.IsType<IOException>(result.Exception);
    }

    [Fact]
    public async Task SaleRemainsCommitted_WhenPrinterIsUnavailableAfterPayment()
    {
        var product = new Product
        {
            ProductCode = "PRD-HW-001", Name = "Hardware isolation product", Unit = UnitOfMeasure.Piece,
            PurchasePrice = 8, Mrp = 12, SellingPrice = 10, IsActive = true,
        }.WithRetailPrice();
        _fixture.Context.Products.Add(product);
        _fixture.Context.Inventories.Add(new Inventory { Product = product, QuantityOnHand = 5 });
        await _fixture.Context.SaveChangesAsync();
        var sales = new SaleService(
            _fixture.Context,
            new EfSequenceGenerator(_fixture.Context),
            new EfAuditLogger(_fixture.Context),
            new PermissionEnforcer(_fixture.Context));

        var sale = await sales.CompleteSaleAsync(new CompleteSaleRequest
        {
            Lines = [new SaleLineInput { ProductId = product.Id, Quantity = 1 }],
            Payments = [new SalePaymentInput { Method = PaymentMethod.Cash, Amount = 10, AmountTendered = 10 }],
        });
        var readiness = await new ReceiptHardwareGuard(new FakeSettings(), new ThrowingPrinter()).CheckAvailabilityAsync();

        Assert.False(readiness.Succeeded);
        Assert.True(await _fixture.Context.Sales.AnyAsync(x => x.Id == sale.Id));
        Assert.Equal(4, (await _fixture.Context.Inventories.SingleAsync()).QuantityOnHand);
    }

    private sealed class FakeSettings : IHardwareSettingsService
    {
        public Task<HardwareSettings> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HardwareSettings { ReceiptPrinterName = "Receipt" });
        public Task SaveAsync(SaveHardwareSettingsRequest request, int? userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordTestAsync(int? userId, HardwareType type, bool succeeded, string detail, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakePrinter(bool available) : IPrinterService
    {
        public Task<IReadOnlyList<HardwareDevice>> GetInstalledPrintersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HardwareDevice>>([]);
        public Task<HardwareDevice?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default) => Task.FromResult<HardwareDevice?>(null);
        public Task<bool> IsPrinterAvailableAsync(string? printerName, CancellationToken cancellationToken = default) => Task.FromResult(available);
        public Task<HardwareOperationResult> PrintTestPageAsync(string? printerName, PrinterPaperSize paperSize, CancellationToken cancellationToken = default) => Task.FromResult(HardwareOperationResult.Success("ok"));
    }

    private sealed class ThrowingPrinter : IPrinterService
    {
        public Task<IReadOnlyList<HardwareDevice>> GetInstalledPrintersAsync(CancellationToken cancellationToken = default) => throw new IOException("Spooler fault");
        public Task<HardwareDevice?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default) => throw new IOException("Spooler fault");
        public Task<bool> IsPrinterAvailableAsync(string? printerName, CancellationToken cancellationToken = default) => throw new IOException("Spooler fault");
        public Task<HardwareOperationResult> PrintTestPageAsync(string? printerName, PrinterPaperSize paperSize, CancellationToken cancellationToken = default) => throw new IOException("Spooler fault");
    }

    public void Dispose() => _fixture.Dispose();
}
