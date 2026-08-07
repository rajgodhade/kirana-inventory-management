using Kirana.Application.Hardware;
using Kirana.Domain.Entities;
using Windows.Devices.Enumeration;

namespace Kirana.App.Hardware;

/// <summary>Discovers identifiable HID scanners and tracks keyboard-wedge health from actual scan
/// traffic. Windows often exposes a wedge scanner as an ordinary keyboard, so Unknown is honest
/// until a device is discoverable or Kirana observes a valid scan.</summary>
public sealed class WindowsScannerService : IScannerService
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastSuccessfulScan;
    // A keyboard-wedge scanner cannot be distinguished reliably from an ordinary keyboard.
    // Treat it as disconnected until Kirana observes a valid scanner-speed barcode burst.
    private HardwareStatus _wedgeStatus = HardwareStatus.Disconnected;
    private string? _lastError;

    public event Action<HardwareStatusChangedEventArgs>? StatusChanged;

    public async Task<IReadOnlyList<HardwareDevice>> GetScannersAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<HardwareDevice>();
        try
        {
            // Windows does not expose one universal "barcode scanner" device class: keyboard-
            // wedge scanners commonly register as keyboards. Enumerate association endpoints and
            // apply conservative name/id heuristics; actual scan traffic remains authoritative.
            var devices = await DeviceInformation.FindAllAsync();
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var device in devices.Where(IsLikelyScanner))
            {
                var bluetooth = IsBluetooth(device);
                results.Add(new HardwareDevice
                {
                    DeviceId = device.Id,
                    Type = bluetooth ? HardwareType.BluetoothHidScanner : HardwareType.UsbHidScanner,
                    FriendlyName = string.IsNullOrWhiteSpace(device.Name) ? "Barcode scanner" : device.Name,
                    Manufacturer = ReadString(device, "System.Devices.Manufacturer"),
                    Model = ReadString(device, "System.Devices.ModelName"),
                    ConnectionType = bluetooth ? HardwareConnectionType.Bluetooth : HardwareConnectionType.Usb,
                    Status = device.IsEnabled ? HardwareStatus.Connected : HardwareStatus.Disconnected,
                    Capabilities = HardwareCapability.ScanBarcode,
                    LastSeen = device.IsEnabled ? DateTimeOffset.UtcNow : null,
                });
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            ReportFailure(ex.Message);
        }

        var wedge = GetKeyboardWedgeStatus();
        if (results.Count == 0 || wedge.LastSeen is not null)
        {
            results.Add(wedge);
        }

        return results;
    }

    public HardwareDevice GetKeyboardWedgeStatus()
    {
        lock (_gate)
        {
            return new HardwareDevice
            {
                DeviceId = "scanner:keyboard-wedge",
                Type = HardwareType.VirtualScanner,
                FriendlyName = "Keyboard-wedge scanner",
                Manufacturer = "Windows HID input",
                ConnectionType = HardwareConnectionType.KeyboardWedge,
                Status = _wedgeStatus,
                Capabilities = HardwareCapability.ScanBarcode,
                LastSeen = _lastSuccessfulScan,
                ErrorMessage = _lastError,
            };
        }
    }

    public void ReportSuccessfulScan(string rawData)
    {
        if (string.IsNullOrWhiteSpace(rawData)) return;
        HardwareStatus previous;
        HardwareDevice current;
        lock (_gate)
        {
            previous = _wedgeStatus;
            _wedgeStatus = HardwareStatus.Connected;
            _lastSuccessfulScan = DateTimeOffset.UtcNow;
            _lastError = null;
            current = GetKeyboardWedgeStatusUnsafe();
        }

        if (previous != HardwareStatus.Connected)
        {
            StatusChanged?.Invoke(new HardwareStatusChangedEventArgs(current, previous, current.Status));
        }
    }

    public void ReportFailure(string message)
    {
        HardwareStatus previous;
        HardwareDevice current;
        lock (_gate)
        {
            previous = _wedgeStatus;
            _wedgeStatus = HardwareStatus.Error;
            _lastError = message;
            current = GetKeyboardWedgeStatusUnsafe();
        }

        if (previous != HardwareStatus.Error)
        {
            StatusChanged?.Invoke(new HardwareStatusChangedEventArgs(current, previous, current.Status));
        }
    }

    private HardwareDevice GetKeyboardWedgeStatusUnsafe() => new()
    {
        DeviceId = "scanner:keyboard-wedge",
        Type = HardwareType.VirtualScanner,
        FriendlyName = "Keyboard-wedge scanner",
        Manufacturer = "Windows HID input",
        ConnectionType = HardwareConnectionType.KeyboardWedge,
        Status = _wedgeStatus,
        Capabilities = HardwareCapability.ScanBarcode,
        LastSeen = _lastSuccessfulScan,
        ErrorMessage = _lastError,
    };

    private static bool IsLikelyScanner(DeviceInformation device)
    {
        var text = $"{device.Name} {device.Id}";
        return text.Contains("scanner", StringComparison.OrdinalIgnoreCase)
            || text.Contains("barcode", StringComparison.OrdinalIgnoreCase)
            || text.Contains("symbol", StringComparison.OrdinalIgnoreCase)
            || text.Contains("honeywell", StringComparison.OrdinalIgnoreCase)
            || text.Contains("zebra", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBluetooth(DeviceInformation device) =>
        $"{device.Name} {device.Id}".Contains("bluetooth", StringComparison.OrdinalIgnoreCase)
        || device.Id.Contains("BTH", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(DeviceInformation device, string key) =>
        device.Properties.TryGetValue(key, out var value) ? value?.ToString() : null;
}
