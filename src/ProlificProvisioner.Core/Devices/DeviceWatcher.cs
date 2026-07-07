namespace ProlificProvisioner.Core.Devices;

public sealed record DeviceChangeEvent(UsbSerialDevice Device, bool Arrived);

/// <summary>
/// Polls <see cref="IUsbDeviceEnumerator"/> on an interval and raises arrival/removal
/// events by diffing against the previous snapshot. Polling (vs. a raw WMI event
/// subscription) keeps this simple, testable with a fake enumerator, and avoids
/// WMI event-consumer permission issues on locked-down assembly-line PCs.
/// </summary>
public sealed class DeviceWatcher : IDisposable
{
    private readonly IUsbDeviceEnumerator _enumerator;
    private readonly TimeSpan _pollInterval;
    private readonly System.Threading.Timer _timer;
    private Dictionary<string, UsbSerialDevice> _lastSnapshot = new();
    private readonly object _lock = new();

    public event EventHandler<DeviceChangeEvent>? DeviceChanged;

    public DeviceWatcher(IUsbDeviceEnumerator enumerator, TimeSpan? pollInterval = null)
    {
        _enumerator = enumerator;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
        _timer = new System.Threading.Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(TimeSpan.Zero, _pollInterval);

    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    /// <summary>Runs one enumerate-and-diff pass immediately; also used directly by tests.</summary>
    public void Poll()
    {
        List<UsbSerialDevice> current;
        try
        {
            current = _enumerator.EnumerateProlificDevices().ToList();
        }
        catch
        {
            // Transient enumeration failures (device mid-enumeration, WMI hiccup) are
            // swallowed here; the next poll tick will retry.
            return;
        }

        lock (_lock)
        {
            var currentById = current.ToDictionary(d => d.DeviceInstanceId);

            foreach (var removedId in _lastSnapshot.Keys.Except(currentById.Keys).ToList())
            {
                DeviceChanged?.Invoke(this, new DeviceChangeEvent(_lastSnapshot[removedId], Arrived: false));
            }

            foreach (var addedId in currentById.Keys.Except(_lastSnapshot.Keys).ToList())
            {
                DeviceChanged?.Invoke(this, new DeviceChangeEvent(currentById[addedId], Arrived: true));
            }

            _lastSnapshot = currentById;
        }
    }

    public void Dispose() => _timer.Dispose();
}
