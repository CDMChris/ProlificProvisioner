using ProlificProvisioner.Core.ComPorts;
using ProlificProvisioner.Core.Config;
using ProlificProvisioner.Core.Devices;
using ProlificProvisioner.Core.Drivers;
using ProlificProvisioner.Core.Logging;

namespace ProlificProvisioner.Core.Workflow;

/// <summary>
/// Top-level wiring: watches for Prolific device arrival/removal, resolves each
/// device to a fixture role via its physical location, and dispatches to the
/// matching <see cref="PortSlotController"/>. This is what the WPF app binds to.
/// </summary>
public sealed class ProvisioningCoordinator : IDisposable
{
    public IReadOnlyDictionary<PortRole, PortSlotController> Slots { get; }

    private readonly DeviceWatcher _watcher;
    private readonly PortRoleResolver _roleResolver;
    private readonly IUsbDeviceEnumerator _enumerator;

    public event EventHandler<string>? UnresolvedDeviceDetected;

    public ProvisioningCoordinator(
        AppConfig config,
        IUsbDeviceEnumerator enumerator,
        DeviceWatcher watcher,
        DriverRollbackService rollbackService,
        ComPortAssigner comPortAssigner,
        ProvisioningLog log)
    {
        _enumerator = enumerator;
        _watcher = watcher;
        _roleResolver = new PortRoleResolver(config);

        Slots = new Dictionary<PortRole, PortSlotController>
        {
            [PortRole.DispenseHead] = new PortSlotController(
                PortRole.DispenseHead, config, rollbackService, comPortAssigner, log, GetCurrentDevices),
            [PortRole.Printer] = new PortSlotController(
                PortRole.Printer, config, rollbackService, comPortAssigner, log, GetCurrentDevices),
        };

        _watcher.DeviceChanged += OnDeviceChanged;
        _watcher.Polled += (_, devices) => ReconcileCurrentDevices(devices);
    }

    public PortRoleResolver RoleResolver => _roleResolver;

    public void Start() => _watcher.Start();

    public void Stop() => _watcher.Stop();

    private IReadOnlyList<UsbSerialDevice> GetCurrentDevices() => _enumerator.EnumerateProlificDevices();

    private void OnDeviceChanged(object? sender, DeviceChangeEvent e)
    {
        var role = _roleResolver.Resolve(e.Device.LocationPath);
        if (role is null)
        {
            if (e.Arrived)
            {
                UnresolvedDeviceDetected?.Invoke(this, e.Device.LocationPath);
            }
            return;
        }

        var slot = Slots[role.Value];
        if (e.Arrived)
        {
            slot.OnDeviceArrived(e.Device);
        }
        else
        {
            slot.OnDeviceRemoved(e.Device);
        }
    }

    /// <summary>
    /// Re-checks every currently-connected device against the role map. Covers the
    /// case a device was already plugged in (and physically never re-arrives) when its
    /// fixture port gets Learned — <see cref="OnDeviceChanged"/> alone would never
    /// revisit it, since nothing about its connection state changes at that moment.
    /// Safe to call every poll tick: <see cref="PortSlotController.OnDeviceArrived"/>
    /// is a no-op for a device it's already tracking.
    /// </summary>
    public void ReconcileCurrentDevices() => ReconcileCurrentDevices(_enumerator.EnumerateProlificDevices());

    private void ReconcileCurrentDevices(IReadOnlyList<UsbSerialDevice> currentDevices)
    {
        foreach (var device in currentDevices)
        {
            var role = _roleResolver.Resolve(device.LocationPath);
            if (role is not null)
            {
                Slots[role.Value].OnDeviceArrived(device);
            }
        }
    }

    public void Dispose() => _watcher.Dispose();
}
