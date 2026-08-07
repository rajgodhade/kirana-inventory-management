using Kirana.Application.Hardware;
using Kirana.Domain.Entities;

namespace Kirana.App.Hardware;

public sealed class WindowsDeviceDiscovery(IPrinterService printerService, IScannerService scannerService)
    : IDeviceDiscoveryService
{
    public async Task<IReadOnlyList<HardwareDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<HardwareDevice>();
        try
        {
            devices.AddRange(await printerService.GetInstalledPrintersAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            devices.Add(FailedDevice("printer:discovery", HardwareType.Printer, "Printer discovery", ex));
        }

        try
        {
            devices.AddRange(await scannerService.GetScannersAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            devices.Add(FailedDevice("scanner:discovery", HardwareType.BarcodeScanner, "Scanner discovery", ex));
        }

        return devices;
    }

    private static HardwareDevice FailedDevice(string id, HardwareType type, string name, Exception exception) => new()
    {
        DeviceId = id,
        Type = type,
        FriendlyName = name,
        Status = HardwareStatus.Error,
        ErrorMessage = exception.Message,
    };
}

public sealed class WindowsDeviceManager(IDeviceDiscoveryService discovery) : DeviceManager(discovery);
