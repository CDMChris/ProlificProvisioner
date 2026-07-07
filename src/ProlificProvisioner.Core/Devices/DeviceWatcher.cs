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

    /// <summary>
    /// Fired after every poll tick, regardless of whether anything changed — the
    /// current full device snapshot. Arrival/removal diffing only captures physical
    /// plug/unplug transitions; a device that was already connected but unresolved
    /// (its fixture port hadn't been Learned yet) never re-fires as an "arrival" once
    /// it becomes resolvable, since nothing about its physical connection state
    /// changed. Subscribers that need to react to "this device is now resolvable"
    /// (not just "this device just appeared") should use this instead.
    /// </summary>
    public event EventHandler<IReadOnlyList<UsbSerialDevice>>? Polled;

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
        try
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
                    RaiseDeviceChanged(new DeviceChangeEvent(_lastSnapshot[removedId], Arrived: false));
                }

                foreach (var addedId in currentById.Keys.Except(_lastSnapshot.Keys).ToList())
                {
                    RaiseDeviceChanged(new DeviceChangeEvent(currentById[addedId], Arrived: true));
                }

                _lastSnapshot = currentById;
            }

            RaisePolled(current);
        }
        catch
        {
            // A subscriber throwing must not take down the polling timer/process —
            // that would silently freeze the whole dashboard until the app is restarted.
        }
    }

    private void RaiseDeviceChanged(DeviceChangeEvent e)
    {
        try
        {
            DeviceChanged?.Invoke(this, e);
        }
        catch
        {
            // One bad subscriber shouldn't stop other subscribers or the next poll tick.
        }
    }

    private void RaisePolled(IReadOnlyList<UsbSerialDevice> current)
    {
        try
        {
            Polled?.Invoke(this, current);
        }
        catch
        {
            // See RaiseDeviceChanged.
        }
    }

    public void Dispose() => _timer.Dispose();
}
