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

    public void Dispose() => _watcher.Dispose();
}
