using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Barcodes;
using Kirana.Application.Hardware;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.App.ViewModels;

public sealed partial class DeviceStatusViewModel(
    IHardwareMonitor monitor,
    IHardwareSettingsService settingsService) : ObservableObject
{
    public ObservableCollection<DeviceStatusRowViewModel> Devices { get; } = [];

    [ObservableProperty] private HardwareStatus _printerStatus = HardwareStatus.Unknown;
    [ObservableProperty] private string _printerDetail = "Checking Windows printers…";
    [ObservableProperty] private HardwareStatus _scannerStatus = HardwareStatus.Unknown;
    [ObservableProperty] private string _scannerDetail = "Waiting for scanner status…";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var settings = await settingsService.GetAsync();
            var devices = await monitor.GetSnapshotAsync();
            Devices.Clear();
            foreach (var device in devices.OrderBy(x => x.Type).ThenBy(x => x.FriendlyName))
            {
                Devices.Add(DeviceStatusRowViewModel.From(device));
            }

            var printers = devices.Where(IsReceiptPrinter).ToList();
            var preferred = printers.FirstOrDefault(x =>
                    string.Equals(x.FriendlyName, settings.ReceiptPrinterName, StringComparison.OrdinalIgnoreCase))
                ?? printers.FirstOrDefault(x => x.IsDefault)
                ?? printers.FirstOrDefault();
            PrinterStatus = preferred?.Status ?? HardwareStatus.Disconnected;
            PrinterDetail = preferred is null ? "No physical receipt printer detected" : preferred.FriendlyName;

            if (!settings.BarcodeScannerEnabled)
            {
                ScannerStatus = HardwareStatus.Offline;
                ScannerDetail = "Scanner input disabled in Hardware Settings";
            }
            else
            {
                var scanners = devices.Where(IsScanner).ToList();
                var scanner = scanners.FirstOrDefault(x => x.Status == HardwareStatus.Connected)
                    ?? scanners.FirstOrDefault();
                ScannerStatus = scanner?.Status ?? HardwareStatus.Unknown;
                ScannerDetail = scanner is null ? "Keyboard-wedge input ready; no scan observed" : scanner.FriendlyName;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Device status is unavailable: {ex.Message}";
            PrinterStatus = HardwareStatus.Error;
            ScannerStatus = HardwareStatus.Error;
        }
        finally { IsBusy = false; }
    }

    private static bool IsReceiptPrinter(HardwareDevice x) =>
        x.Type is HardwareType.Printer or HardwareType.ThermalPrinter
        && x.Capabilities.HasFlag(HardwareCapability.PrintReceipt);
    private static bool IsScanner(HardwareDevice x) => x.Type is HardwareType.BarcodeScanner or HardwareType.UsbHidScanner
        or HardwareType.BluetoothHidScanner or HardwareType.VirtualScanner or HardwareType.MobileScanner;
}

public sealed class DeviceStatusRowViewModel
{
    public string DeviceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Connection { get; init; } = string.Empty;
    public string ManufacturerModel { get; init; } = "Not reported by Windows";
    public HardwareStatus Status { get; init; }
    public string StatusText => Status.ToString();
    public string LastSeenText { get; init; } = "Not seen yet";
    public bool IsDefault { get; init; }
    public string DefaultText => IsDefault ? "Default" : string.Empty;

    public static DeviceStatusRowViewModel From(HardwareDevice device) => new()
    {
        DeviceId = device.DeviceId,
        Name = device.FriendlyName,
        Type = device.Type.ToString(),
        Connection = device.ConnectionType.ToString(),
        ManufacturerModel = string.Join(" · ", new[] { device.Manufacturer, device.Model }.Where(x => !string.IsNullOrWhiteSpace(x))),
        Status = device.Status,
        LastSeenText = device.LastSeen is { } seen ? $"Last seen {seen.ToLocalTime():dd MMM, hh:mm:ss tt}" : "Not seen yet",
        IsDefault = device.IsDefault,
    };
}

public sealed partial class HardwareSettingsViewModel(
    IPrinterService printerService,
    IScannerService scannerService,
    IHardwareSettingsService settingsService,
    IHardwareMonitor monitor,
    IBarcodeLookupService barcodeLookup,
    ManagementSession session) : ObservableObject
{
    public ObservableCollection<string> Printers { get; } = [];
    public ObservableCollection<string> ReceiptPrinters { get; } = [];
    public IReadOnlyList<PrinterPaperSize> PaperSizes { get; } = Enum.GetValues<PrinterPaperSize>();
    public DeviceStatusViewModel DeviceStatus { get; } = new(monitor, settingsService);

    public bool CanManage => session.HasPermission(PermissionKeys.HardwareManage);

    [ObservableProperty] private string? _selectedDefaultPrinter;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTestPrint))]
    private string? _selectedReceiptPrinter;
    [ObservableProperty] private string? _selectedInvoicePrinter;
    [ObservableProperty] private PrinterPaperSize _selectedPaperSize = PrinterPaperSize.Thermal80mm;
    [ObservableProperty] private bool _autoPrintReceipt;
    [ObservableProperty] private bool _printDuplicateCopy;
    [ObservableProperty] private bool _openCashDrawerAfterCashPayment;
    [ObservableProperty] private bool _barcodeScannerEnabled = true;
    [ObservableProperty] private bool _enableSoundOnScan = true;
    [ObservableProperty] private bool _autoFocusScannerInput = true;
    [ObservableProperty] private string _scannerTimeoutText = "40";
    [ObservableProperty] private string _scannerTestInput = string.Empty;
    [ObservableProperty] private string _rawScanData = "—";
    [ObservableProperty] private string _detectedBarcode = "—";
    [ObservableProperty] private string _lookupResult = "Scan a product barcode to test lookup.";
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    public bool CanTestPrint => CanManage && !string.IsNullOrWhiteSpace(SelectedReceiptPrinter);

    public async Task InitializeAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var settings = await settingsService.GetAsync();
            var printers = await printerService.GetInstalledPrintersAsync();
            Printers.Clear();
            ReceiptPrinters.Clear();
            foreach (var printer in printers)
            {
                Printers.Add(printer.FriendlyName);
                if (printer.Capabilities.HasFlag(HardwareCapability.PrintReceipt))
                {
                    ReceiptPrinters.Add(printer.FriendlyName);
                }
            }

            SelectedDefaultPrinter = ResolvePrinter(Printers, settings.DefaultPrinterName, printers.FirstOrDefault(x => x.IsDefault)?.FriendlyName);
            SelectedReceiptPrinter = ResolvePrinter(ReceiptPrinters, settings.ReceiptPrinterName, null);
            SelectedInvoicePrinter = ResolvePrinter(Printers, settings.InvoicePrinterName, SelectedDefaultPrinter);
            SelectedPaperSize = settings.ReceiptPaperSize;
            AutoPrintReceipt = settings.AutoPrintReceipt;
            PrintDuplicateCopy = settings.PrintDuplicateCopy;
            OpenCashDrawerAfterCashPayment = settings.OpenCashDrawerAfterCashPayment;
            BarcodeScannerEnabled = settings.BarcodeScannerEnabled;
            EnableSoundOnScan = settings.EnableSoundOnScan;
            AutoFocusScannerInput = settings.AutoFocusScannerInput;
            ScannerTimeoutText = settings.ScannerTimeoutMilliseconds.ToString();
            await DeviceStatus.RefreshAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await InitializeAsync();

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        if (!int.TryParse(ScannerTimeoutText, out var timeout) || timeout is < 10 or > 2000)
        {
            ErrorMessage = "Scanner timeout must be a whole number between 10 and 2000 milliseconds.";
            return;
        }

        try
        {
            await settingsService.SaveAsync(new SaveHardwareSettingsRequest
            {
                DefaultPrinterName = SelectedDefaultPrinter,
                ReceiptPrinterName = SelectedReceiptPrinter,
                InvoicePrinterName = SelectedInvoicePrinter,
                ReceiptPaperSize = SelectedPaperSize,
                AutoPrintReceipt = AutoPrintReceipt,
                PrintDuplicateCopy = PrintDuplicateCopy,
                OpenCashDrawerAfterCashPayment = OpenCashDrawerAfterCashPayment,
                BarcodeScannerEnabled = BarcodeScannerEnabled,
                EnableSoundOnScan = EnableSoundOnScan,
                AutoFocusScannerInput = AutoFocusScannerInput,
                ScannerTimeoutMilliseconds = timeout,
            }, session.CurrentUser?.Id);
            StatusMessage = "Hardware settings saved.";
            await DeviceStatus.RefreshAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task TestPrintAsync()
    {
        ErrorMessage = null;
        StatusMessage = "Sending test page…";
        var result = await printerService.PrintTestPageAsync(SelectedReceiptPrinter, SelectedPaperSize);
        await settingsService.RecordTestAsync(session.CurrentUser?.Id, HardwareType.Printer, result.Succeeded, result.Message);
        StatusMessage = result.Succeeded ? result.Message : null;
        ErrorMessage = result.Succeeded ? null : result.Message;
        await DeviceStatus.RefreshAsync();
    }

    [RelayCommand]
    private async Task TestScannerAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;
        var raw = ScannerTestInput;
        var barcode = raw.Trim();
        RawScanData = string.IsNullOrEmpty(raw) ? "—" : raw;
        DetectedBarcode = string.IsNullOrEmpty(barcode) ? "—" : barcode;
        if (string.IsNullOrEmpty(barcode))
        {
            ErrorMessage = "Scan or enter a barcode first.";
            return;
        }

        try
        {
            scannerService.ReportSuccessfulScan(barcode);
            var product = await barcodeLookup.LookupAsync(barcode);
            LookupResult = product is null
                ? "No active product matches this barcode. Scanner input was received correctly."
                : $"Found: {product.Name} · {product.ProductCode} · ₹{product.SellingPrice:N2}";
            await settingsService.RecordTestAsync(session.CurrentUser?.Id, HardwareType.BarcodeScanner, true,
                $"Scanner test received {barcode.Length} characters.");
            StatusMessage = "Scanner input detected successfully.";
            ScannerTestInput = string.Empty;
            await DeviceStatus.RefreshAsync();
        }
        catch (Exception ex)
        {
            scannerService.ReportFailure(ex.Message);
            ErrorMessage = ex.Message;
            await settingsService.RecordTestAsync(session.CurrentUser?.Id, HardwareType.BarcodeScanner, false, ex.Message);
        }
    }

    private static string? ResolvePrinter(IEnumerable<string> options, string? requested, string? fallback) =>
        options.FirstOrDefault(x => string.Equals(x, requested, StringComparison.OrdinalIgnoreCase))
        ?? options.FirstOrDefault(x => string.Equals(x, fallback, StringComparison.OrdinalIgnoreCase))
        ?? options.FirstOrDefault();
}

public sealed partial class HardwareDiagnosticsViewModel(
    IHardwareMonitor monitor,
    IHardwareSettingsService settingsService,
    IKiranaDbContext db,
    IBackupService backupService,
    ManagementSession session) : ObservableObject
{
    public ObservableCollection<DiagnosticRowViewModel> Results { get; } = [];
    public bool CanManage => session.HasPermission(PermissionKeys.HardwareManage);
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _lastRunText = "Not run yet";

    [RelayCommand]
    public async Task RunAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        Results.Clear();
        try
        {
            var devices = await monitor.GetSnapshotAsync();
            AddDeviceResult("Printer", devices.Where(IsPrinter).ToList());
            AddDeviceResult("Scanner", devices.Where(IsScanner).ToList());

            try
            {
                await db.AppSettings.AsNoTracking().AnyAsync();
                Add("Database", HardwareStatus.Connected, "SQLite database opened successfully.");
            }
            catch (Exception ex) { Add("Database", HardwareStatus.Error, ex.Message); }

            var online = NetworkInterface.GetIsNetworkAvailable();
            Add("Internet", online ? HardwareStatus.Connected : HardwareStatus.Offline,
                online ? "A Windows network connection is available." : "Windows reports no network connection. Local billing is unaffected.");
            Add("Windows", HardwareStatus.Connected, RuntimeInformation.OSDescription);
            Add(".NET", HardwareStatus.Connected, RuntimeInformation.FrameworkDescription);
            Add("SQLite", HardwareStatus.Connected,
                $"Microsoft.Data.Sqlite {typeof(Microsoft.Data.Sqlite.SqliteConnection).Assembly.GetName().Version}");
            Add("Application", HardwareStatus.Connected,
                typeof(App).Assembly.GetName().Version?.ToString() ?? "Version unavailable");

            var backups = await backupService.GetHistoryAsync();
            var latest = backups.OrderByDescending(x => x.Record.CreatedAtUtc).FirstOrDefault();
            Add("Last backup", latest is null ? HardwareStatus.Unknown : HardwareStatus.Connected,
                latest is null ? "No backup recorded." : latest.Record.CreatedAtUtc.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt"));

            var appSettings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
            Add("Last maintenance", appSettings?.LastHardwareMaintenanceUtc is null ? HardwareStatus.Unknown : HardwareStatus.Connected,
                appSettings?.LastHardwareMaintenanceUtc?.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt") ?? "No hardware maintenance recorded.");
            LastRunText = $"Completed {DateTime.Now:dd MMM yyyy, hh:mm:ss tt}";
            await settingsService.RecordTestAsync(session.CurrentUser?.Id, HardwareType.VirtualScanner, true, "Full hardware diagnostics completed.");
        }
        catch (Exception ex) { ErrorMessage = $"Diagnostics could not complete: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public string BuildReport()
    {
        var report = new StringBuilder()
            .AppendLine("KIRANA HARDWARE DIAGNOSTICS")
            .AppendLine(LastRunText)
            .AppendLine(new string('-', 60));
        foreach (var row in Results) report.AppendLine($"{row.Component}: {row.Status} - {row.Detail}");
        return report.ToString();
    }

    private void AddDeviceResult(string label, IReadOnlyList<HardwareDevice> devices)
    {
        var best = devices.FirstOrDefault(x => x.Status == HardwareStatus.Connected) ?? devices.FirstOrDefault();
        Add(label, best?.Status ?? HardwareStatus.Disconnected,
            best is null ? $"No {label.ToLowerInvariant()} detected." : $"{best.FriendlyName} · {best.ConnectionType}");
    }

    private void Add(string component, HardwareStatus status, string detail) =>
        Results.Add(new DiagnosticRowViewModel(component, status, detail));

    private static bool IsPrinter(HardwareDevice x) =>
        x.Type is HardwareType.Printer or HardwareType.ThermalPrinter
        && x.Capabilities.HasFlag(HardwareCapability.PrintReceipt);
    private static bool IsScanner(HardwareDevice x) => x.Type is HardwareType.BarcodeScanner or HardwareType.UsbHidScanner
        or HardwareType.BluetoothHidScanner or HardwareType.VirtualScanner or HardwareType.MobileScanner;
}

public sealed record DiagnosticRowViewModel(string Component, HardwareStatus Status, string Detail)
{
    public string StatusText => Status.ToString();
}
