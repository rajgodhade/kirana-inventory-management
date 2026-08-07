using Kirana.Application.Hardware;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Kirana.Infrastructure.Persistence;
using Kirana.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Tests.Hardware;

public sealed class HardwareSettingsServiceTests : IDisposable
{
    private readonly SqliteDbContextFixture _fixture = new();

    [Fact]
    public async Task OwnerCanPersistPrinterAndScannerSettings_WithAudit()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var sut = CreateService();

        await sut.SaveAsync(new SaveHardwareSettingsRequest
        {
            DefaultPrinterName = "Microsoft Print to PDF",
            ReceiptPrinterName = "Thermal 80",
            InvoicePrinterName = "Office A4",
            ReceiptPaperSize = PrinterPaperSize.Thermal80mm,
            AutoPrintReceipt = true,
            PrintDuplicateCopy = true,
            BarcodeScannerEnabled = false,
            EnableSoundOnScan = false,
            AutoFocusScannerInput = false,
            ScannerTimeoutMilliseconds = 75,
        }, owner.Id);

        _fixture.Context.ChangeTracker.Clear();
        var saved = await sut.GetAsync();
        Assert.Equal("Thermal 80", saved.ReceiptPrinterName);
        Assert.Equal("Office A4", saved.InvoicePrinterName);
        Assert.True(saved.AutoPrintReceipt);
        Assert.True(saved.PrintDuplicateCopy);
        Assert.False(saved.BarcodeScannerEnabled);
        Assert.Equal(75, saved.ScannerTimeoutMilliseconds);
        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(x => x.Action == "PrinterChanged"));
        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(x => x.Action == "ScannerDisabled"));
    }

    [Fact]
    public async Task CashierCanViewButCannotChangeHardwareSettings()
    {
        await _fixture.SeedOwnerAsync();
        var cashier = await _fixture.SeedCashierAsync();
        var sut = CreateService();

        var current = await sut.GetAsync();
        Assert.True(current.BarcodeScannerEnabled);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.SaveAsync(
            new SaveHardwareSettingsRequest { ScannerTimeoutMilliseconds = 40 }, cashier.Id));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(2001)]
    public async Task InvalidScannerTimeout_IsRejected(int timeout)
    {
        var owner = await _fixture.SeedOwnerAsync();
        var sut = CreateService();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.SaveAsync(
            new SaveHardwareSettingsRequest { ScannerTimeoutMilliseconds = timeout }, owner.Id));
    }

    [Fact]
    public async Task HardwareTestAndFailure_AreAudited()
    {
        var owner = await _fixture.SeedOwnerAsync();
        var sut = CreateService();

        await sut.RecordTestAsync(owner.Id, HardwareType.Printer, true, "Test page sent");
        await sut.RecordTestAsync(owner.Id, HardwareType.BarcodeScanner, false, "HID read failed");

        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(x => x.Action == "HardwareTest"));
        Assert.True(await _fixture.Context.AuditLogs.AnyAsync(x => x.Action == "ScannerFailure"));
    }

    private HardwareSettingsService CreateService() => new(
        _fixture.Context,
        new PermissionEnforcer(_fixture.Context),
        new EfAuditLogger(_fixture.Context));

    public void Dispose() => _fixture.Dispose();
}
