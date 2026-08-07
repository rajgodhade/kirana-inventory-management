using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Hardware;

public sealed class HardwareSettingsService(
    IKiranaDbContext db,
    IPermissionEnforcer permissionEnforcer,
    IAuditLogger auditLogger) : IHardwareSettingsService
{
    public async Task<HardwareSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return settings is null ? new HardwareSettings() : Map(settings);
    }

    public async Task SaveAsync(
        SaveHardwareSettingsRequest request,
        int? userId,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(userId, PermissionKeys.HardwareManage, cancellationToken);
        if (request.ScannerTimeoutMilliseconds is < 10 or > 2000)
        {
            throw new ArgumentOutOfRangeException(nameof(request.ScannerTimeoutMilliseconds), "Scanner timeout must be between 10 and 2000 milliseconds.");
        }

        var settings = await db.AppSettings.FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Application settings are not initialized.");
        var before = Map(settings);

        settings.DefaultPrinterName = Clean(request.DefaultPrinterName);
        settings.ReceiptPrinterName = Clean(request.ReceiptPrinterName);
        settings.InvoicePrinterName = Clean(request.InvoicePrinterName);
        settings.ReceiptPaperSize = request.ReceiptPaperSize;
        settings.AutoPrintReceipt = request.AutoPrintReceipt;
        settings.PrintDuplicateCopy = request.PrintDuplicateCopy;
        settings.OpenCashDrawerAfterCashPayment = request.OpenCashDrawerAfterCashPayment;
        settings.BarcodeScannerEnabled = request.BarcodeScannerEnabled;
        settings.EnableSoundOnScan = request.EnableSoundOnScan;
        settings.AutoFocusScannerInput = request.AutoFocusScannerInput;
        settings.ScannerTimeoutMilliseconds = request.ScannerTimeoutMilliseconds;
        settings.LastHardwareMaintenanceUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        if (!string.Equals(before.DefaultPrinterName, settings.DefaultPrinterName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(before.ReceiptPrinterName, settings.ReceiptPrinterName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(before.InvoicePrinterName, settings.InvoicePrinterName, StringComparison.OrdinalIgnoreCase))
        {
            await auditLogger.RecordAsync(userId, "PrinterChanged", nameof(AppSettings), settings.Id.ToString(),
                before.DefaultPrinterName, settings.DefaultPrinterName, cancellationToken: cancellationToken);
        }

        if (before.BarcodeScannerEnabled != settings.BarcodeScannerEnabled)
        {
            await auditLogger.RecordAsync(userId,
                settings.BarcodeScannerEnabled ? "ScannerEnabled" : "ScannerDisabled",
                nameof(AppSettings), settings.Id.ToString(), cancellationToken: cancellationToken);
        }
    }

    public async Task RecordTestAsync(
        int? userId,
        HardwareType type,
        bool succeeded,
        string detail,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(userId, PermissionKeys.HardwareManage, cancellationToken);
        var settings = await db.AppSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
        {
            settings.LastHardwareMaintenanceUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        var action = succeeded ? "HardwareTest" : type switch
        {
            HardwareType.Printer or HardwareType.ThermalPrinter or HardwareType.A4Printer => "PrinterFailure",
            HardwareType.BarcodeScanner or HardwareType.UsbHidScanner or HardwareType.BluetoothHidScanner or HardwareType.VirtualScanner => "ScannerFailure",
            _ => "HardwareFailure",
        };
        await auditLogger.RecordAsync(userId, action, nameof(HardwareDevice), type.ToString(), reason: detail,
            cancellationToken: cancellationToken);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static HardwareSettings Map(AppSettings settings) => new()
    {
        DefaultPrinterName = settings.DefaultPrinterName,
        ReceiptPrinterName = settings.ReceiptPrinterName,
        InvoicePrinterName = settings.InvoicePrinterName,
        ReceiptPaperSize = settings.ReceiptPaperSize,
        AutoPrintReceipt = settings.AutoPrintReceipt,
        PrintDuplicateCopy = settings.PrintDuplicateCopy,
        OpenCashDrawerAfterCashPayment = settings.OpenCashDrawerAfterCashPayment,
        BarcodeScannerEnabled = settings.BarcodeScannerEnabled,
        EnableSoundOnScan = settings.EnableSoundOnScan,
        AutoFocusScannerInput = settings.AutoFocusScannerInput,
        ScannerTimeoutMilliseconds = settings.ScannerTimeoutMilliseconds,
    };
}
