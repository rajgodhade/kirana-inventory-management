using Kirana.Application.Hardware;
using Kirana.Domain.Entities;

namespace Kirana.Tests.Hardware;

public class DeviceManagerTests
{
    [Fact]
    public async Task RefreshAsync_DiscoversPrinterAndScanner()
    {
        var discovery = new FakeDiscovery([
            Device("printer:1", HardwareType.ThermalPrinter, HardwareStatus.Connected),
            Device("scanner:1", HardwareType.UsbHidScanner, HardwareStatus.Connected),
        ]);
        var sut = new DeviceManager(discovery);

        var devices = await sut.RefreshAsync();

        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, x => x.Type == HardwareType.ThermalPrinter);
        Assert.Contains(devices, x => x.Type == HardwareType.UsbHidScanner);
    }

    [Fact]
    public async Task InitialDiscovery_DoesNotAnnounceAlreadyInstalledDevicesAsNewConnections()
    {
        var sut = new DeviceManager(new FakeDiscovery([
            Device("printer:1", HardwareType.ThermalPrinter, HardwareStatus.Connected),
            Device("scanner:1", HardwareType.UsbHidScanner, HardwareStatus.Disconnected),
        ]));
        var changes = new List<HardwareStatusChangedEventArgs>();
        sut.StatusChanged += changes.Add;

        await sut.RefreshAsync();

        Assert.Empty(changes);
    }

    [Fact]
    public async Task RefreshAsync_RaisesStatusChange_WhenPrinterDisconnects()
    {
        var discovery = new SequencedDiscovery(
            (IReadOnlyList<HardwareDevice>)[Device("printer:1", HardwareType.Printer, HardwareStatus.Connected)],
            (IReadOnlyList<HardwareDevice>)[Device("printer:1", HardwareType.Printer, HardwareStatus.Offline)]);
        var sut = new DeviceManager(discovery);
        var changes = new List<HardwareStatusChangedEventArgs>();
        sut.StatusChanged += changes.Add;

        await sut.RefreshAsync();
        changes.Clear();
        await sut.RefreshAsync();

        var change = Assert.Single(changes);
        Assert.Equal(HardwareStatus.Connected, change.PreviousStatus);
        Assert.Equal(HardwareStatus.Offline, change.CurrentStatus);
    }

    [Fact]
    public async Task RefreshAsync_RaisesDisconnected_WhenDeviceDisappears()
    {
        var discovery = new SequencedDiscovery(
            (IReadOnlyList<HardwareDevice>)[Device("scanner:1", HardwareType.BluetoothHidScanner, HardwareStatus.Connected)],
            (IReadOnlyList<HardwareDevice>)[]);
        var sut = new DeviceManager(discovery);
        HardwareStatusChangedEventArgs? change = null;
        sut.StatusChanged += value => change = value;

        await sut.RefreshAsync();
        change = null;
        await sut.RefreshAsync();

        Assert.NotNull(change);
        Assert.Equal(HardwareStatus.Disconnected, change.CurrentStatus);
    }

    [Fact]
    public async Task DiscoveryFailure_DoesNotEscape_AndMarksKnownDeviceError()
    {
        var discovery = new SequencedDiscovery(
            (IReadOnlyList<HardwareDevice>)[Device("printer:1", HardwareType.Printer, HardwareStatus.Connected)],
            new IOException("Spooler unavailable"));
        var sut = new DeviceManager(discovery);
        await sut.RefreshAsync();

        var devices = await sut.RefreshAsync();

        var printer = Assert.Single(devices);
        Assert.Equal(HardwareStatus.Error, printer.Status);
        Assert.Contains("Spooler", printer.ErrorMessage ?? string.Empty);
    }

    [Fact]
    public async Task InitialDiscoveryFailure_ReturnsEmptySnapshot_InsteadOfCrashing()
    {
        var sut = new DeviceManager(new ThrowingDiscovery());
        var devices = await sut.RefreshAsync();
        Assert.Empty(devices);
    }

    [Fact]
    public async Task MonitorSnapshot_DelegatesToManager()
    {
        var manager = new DeviceManager(new FakeDiscovery([
            Device("scanner:wedge", HardwareType.VirtualScanner, HardwareStatus.Unknown),
        ]));
        using var monitor = new HardwareMonitor(manager, TimeSpan.FromHours(1));

        var snapshot = await monitor.GetSnapshotAsync();

        Assert.Single(snapshot);
        Assert.Equal(HardwareStatus.Unknown, snapshot[0].Status);
    }

    private static HardwareDevice Device(string id, HardwareType type, HardwareStatus status) => new()
    {
        DeviceId = id,
        Type = type,
        FriendlyName = id,
        Status = status,
    };

    private sealed class FakeDiscovery(IReadOnlyList<HardwareDevice> devices) : IDeviceDiscoveryService
    {
        public Task<IReadOnlyList<HardwareDevice>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(devices);
    }

    private sealed class ThrowingDiscovery : IDeviceDiscoveryService
    {
        public Task<IReadOnlyList<HardwareDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("Device API failed");
    }

    private sealed class SequencedDiscovery : IDeviceDiscoveryService
    {
        private readonly Queue<object> _results;
        public SequencedDiscovery(params object[] results) => _results = new Queue<object>(results);
        public Task<IReadOnlyList<HardwareDevice>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            var next = _results.Dequeue();
            return next is Exception exception
                ? Task.FromException<IReadOnlyList<HardwareDevice>>(exception)
                : Task.FromResult((IReadOnlyList<HardwareDevice>)next);
        }
    }
}
