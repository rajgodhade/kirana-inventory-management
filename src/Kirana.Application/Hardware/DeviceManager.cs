using Kirana.Domain.Entities;

namespace Kirana.Application.Hardware;

/// <summary>Platform-neutral device catalogue. Windows discovery is injected, making status
/// transitions and fault behavior deterministic in tests and reusable for future platforms.</summary>
public class DeviceManager(IDeviceDiscoveryService discovery) : IDeviceManager
{
    private readonly object _gate = new();
    private IReadOnlyList<HardwareDevice> _devices = [];
    private bool _hasCompletedInitialDiscovery;

    public event Action<HardwareStatusChangedEventArgs>? StatusChanged;

    public IReadOnlyList<HardwareDevice> Devices
    {
        get { lock (_gate) return _devices.ToList(); }
    }

    public HardwareDevice? Find(HardwareType type) => Devices.FirstOrDefault(x => x.Type == type);

    public async Task<IReadOnlyList<HardwareDevice>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HardwareDevice> discovered;
        try
        {
            discovered = await discovery.DiscoverAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            discovered = Devices.Select(x => x with
            {
                Status = HardwareStatus.Error,
                ErrorMessage = ex.Message,
            }).ToList();
        }

        IReadOnlyList<HardwareDevice> previous;
        bool announceChanges;
        lock (_gate)
        {
            previous = _devices;
            _devices = discovered;
            announceChanges = _hasCompletedInitialDiscovery;
            _hasCompletedInitialDiscovery = true;
        }

        if (announceChanges)
        {
            foreach (var current in discovered)
            {
                var old = previous.FirstOrDefault(x => x.DeviceId == current.DeviceId);
                var oldStatus = old?.Status ?? HardwareStatus.Unknown;
                if (oldStatus != current.Status)
                {
                    StatusChanged?.Invoke(new HardwareStatusChangedEventArgs(current, oldStatus, current.Status));
                }
            }

            foreach (var missing in previous.Where(x => discovered.All(d => d.DeviceId != x.DeviceId)))
            {
                var disconnected = missing with { Status = HardwareStatus.Disconnected };
                StatusChanged?.Invoke(new HardwareStatusChangedEventArgs(disconnected, missing.Status, HardwareStatus.Disconnected));
            }
        }

        return Devices;
    }
}

public sealed class HardwareMonitor(IDeviceManager deviceManager, TimeSpan? pollInterval = null) : IHardwareMonitor, IDisposable
{
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromSeconds(15);
    private CancellationTokenSource? _loopCancellation;
    private Task? _loop;

    public event Action<HardwareStatusChangedEventArgs>? StatusChanged;

    public Task<IReadOnlyList<HardwareDevice>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        deviceManager.RefreshAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loop is not null)
        {
            return Task.CompletedTask;
        }

        _loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deviceManager.StatusChanged += OnStatusChanged;
        _loop = MonitorAsync(_loopCancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_loopCancellation is null || _loop is null)
        {
            return;
        }

        await _loopCancellation.CancelAsync();
        try { await _loop; }
        catch (OperationCanceledException) { }
        deviceManager.StatusChanged -= OnStatusChanged;
        _loopCancellation.Dispose();
        _loopCancellation = null;
        _loop = null;
    }

    private void OnStatusChanged(HardwareStatusChangedEventArgs e) => StatusChanged?.Invoke(e);

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_pollInterval);
        while (!cancellationToken.IsCancellationRequested)
        {
            await deviceManager.RefreshAsync(cancellationToken);
            await timer.WaitForNextTickAsync(cancellationToken);
        }
    }

    public void Dispose()
    {
        _loopCancellation?.Cancel();
        _loopCancellation?.Dispose();
        deviceManager.StatusChanged -= OnStatusChanged;
    }
}
