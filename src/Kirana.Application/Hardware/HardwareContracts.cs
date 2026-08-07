using Kirana.Domain.Entities;

namespace Kirana.Application.Hardware;

public sealed record HardwareOperationResult(bool Succeeded, string Message, Exception? Exception = null)
{
    public static HardwareOperationResult Success(string message) => new(true, message);
    public static HardwareOperationResult Failure(string message, Exception? exception = null) => new(false, message, exception);
}

public record HardwareSettings
{
    public string? DefaultPrinterName { get; init; }
    public string? ReceiptPrinterName { get; init; }
    public string? InvoicePrinterName { get; init; }
    public PrinterPaperSize ReceiptPaperSize { get; init; } = PrinterPaperSize.Thermal80mm;
    public bool AutoPrintReceipt { get; init; }
    public bool PrintDuplicateCopy { get; init; }
    public bool OpenCashDrawerAfterCashPayment { get; init; }
    public bool BarcodeScannerEnabled { get; init; } = true;
    public bool EnableSoundOnScan { get; init; } = true;
    public bool AutoFocusScannerInput { get; init; } = true;
    public int ScannerTimeoutMilliseconds { get; init; } = 40;
}

public sealed record SaveHardwareSettingsRequest : HardwareSettings;

public sealed record HardwareStatusChangedEventArgs(
    HardwareDevice Device,
    HardwareStatus PreviousStatus,
    HardwareStatus CurrentStatus);

public interface IPrinterService
{
    Task<IReadOnlyList<HardwareDevice>> GetInstalledPrintersAsync(CancellationToken cancellationToken = default);
    Task<HardwareDevice?> GetDefaultPrinterAsync(CancellationToken cancellationToken = default);
    Task<bool> IsPrinterAvailableAsync(string? printerName, CancellationToken cancellationToken = default);
    Task<HardwareOperationResult> PrintTestPageAsync(
        string? printerName,
        PrinterPaperSize paperSize,
        CancellationToken cancellationToken = default);
}

public interface IScannerService
{
    event Action<HardwareStatusChangedEventArgs>? StatusChanged;
    Task<IReadOnlyList<HardwareDevice>> GetScannersAsync(CancellationToken cancellationToken = default);
    HardwareDevice GetKeyboardWedgeStatus();
    void ReportSuccessfulScan(string rawData);
    void ReportFailure(string message);
}

public interface IDeviceDiscoveryService
{
    Task<IReadOnlyList<HardwareDevice>> DiscoverAsync(CancellationToken cancellationToken = default);
}

public interface IDeviceManager
{
    event Action<HardwareStatusChangedEventArgs>? StatusChanged;
    IReadOnlyList<HardwareDevice> Devices { get; }
    Task<IReadOnlyList<HardwareDevice>> RefreshAsync(CancellationToken cancellationToken = default);
    HardwareDevice? Find(HardwareType type);
}

public interface IHardwareMonitor
{
    event Action<HardwareStatusChangedEventArgs>? StatusChanged;
    Task<IReadOnlyList<HardwareDevice>> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}

public interface IHardwareSettingsService
{
    Task<HardwareSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(SaveHardwareSettingsRequest request, int? userId, CancellationToken cancellationToken = default);
    Task RecordTestAsync(int? userId, HardwareType type, bool succeeded, string detail, CancellationToken cancellationToken = default);
}

public interface IReceiptHardwareGuard
{
    Task<HardwareOperationResult> CheckAvailabilityAsync(CancellationToken cancellationToken = default);
}

/// <summary>Transport-neutral input contract reserved for mobile/WebSocket scanners and future
/// payment/scale adapters. POS code consumes barcodes, never a USB/Bluetooth API.</summary>
public interface IBarcodeInputSource
{
    HardwareType SourceType { get; }
    event EventHandler<string>? BarcodeReceived;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
