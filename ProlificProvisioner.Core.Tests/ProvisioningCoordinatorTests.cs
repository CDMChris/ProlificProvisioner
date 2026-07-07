using ProlificProvisioner.Core.ComPorts;
using ProlificProvisioner.Core.Config;
using ProlificProvisioner.Core.Devices;
using ProlificProvisioner.Core.Drivers;
using ProlificProvisioner.Core.Logging;
using ProlificProvisioner.Core.Tests.Fakes;
using ProlificProvisioner.Core.Workflow;
using Xunit;

namespace ProlificProvisioner.Core.Tests;

/// <summary>
/// Regression coverage for the reported bug: a cable that's already plugged in when
/// its physical fixture port gets mapped via Learn Ports never started provisioning
/// until the whole app was restarted. Root cause: DeviceWatcher only fires
/// arrival/removal on a snapshot diff, and the device's connection state never
/// changes at the moment it becomes resolvable — so the one-shot "unresolved, skip"
/// path in ProvisioningCoordinator.OnDeviceChanged never got revisited.
/// </summary>
public class ProvisioningCoordinatorTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly string _logPath;

    public ProvisioningCoordinatorTests()
    {
        _logPath = Path.Combine(Path.GetTempPath(), $"provisioning-log-{Guid.NewGuid()}.csv");
        _tempFiles.Add(_logPath);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private string CreateTempInf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"driver-{Guid.NewGuid()}.inf");
        File.WriteAllText(path, "[Version]\n; test placeholder\n");
        _tempFiles.Add(path);
        return path;
    }

    private static async Task<ProvisioningStatus> WaitForTerminalStatus(PortSlotController controller, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            if (controller.Status.IsSuccess || controller.Status.IsTerminalFailure)
            {
                return controller.Status;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Controller never reached a terminal status; stuck at {controller.Status.Step}.");
    }

    [Fact]
    public async Task DeviceAlreadyConnectedBeforeLearning_StartsProvisioningOnceLearnedAndReconciled()
    {
        var config = new AppConfig
        {
            DispenseHeadRollbackDriverInfPath = CreateTempInf(),
            PrinterLatestDriverInfPath = CreateTempInf(),
            MaxAutoRetries = 1,
            DriverStepTimeout = TimeSpan.FromSeconds(5),
        };

        var enumerator = new FakeUsbDeviceEnumerator();
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2304\\1", "USB\\VID_067B&PID_2304", "Port_#0002.Hub_#0001", null);
        enumerator.Devices.Add(device);

        var watcher = new DeviceWatcher(enumerator, pollInterval: TimeSpan.FromMilliseconds(50));
        var driverBinder = new FakeDriverBinder();
        var rollbackService = new DriverRollbackService(driverBinder);
        var comPortAssigner = new ComPortAssigner(driverBinder, new FakeComDb());
        var log = new ProvisioningLog(_logPath);

        var coordinator = new ProvisioningCoordinator(config, enumerator, watcher, rollbackService, comPortAssigner, log);

        string? unresolvedLocation = null;
        coordinator.UnresolvedDeviceDetected += (_, location) => unresolvedLocation = location;

        // First poll: device is connected but its port hasn't been Learned yet.
        watcher.Poll();
        Assert.Equal("Port_#0002.Hub_#0001", unresolvedLocation);
        Assert.Equal(ProvisioningStep.WaitingForCable, coordinator.Slots[PortRole.Printer].Status.Step);

        // Operator now runs Learn Ports and tags this physical port — the cable itself
        // never gets unplugged/replugged, so DeviceWatcher's diff would never re-fire
        // an "arrival" for it on its own.
        coordinator.RoleResolver.Learn(device.LocationPath, PortRole.Printer);

        // The next poll tick must pick it up via reconciliation, not just diffing.
        watcher.Poll();

        var status = await WaitForTerminalStatus(coordinator.Slots[PortRole.Printer]);
        Assert.True(status.IsSuccess);

        coordinator.Dispose();
    }

    [Fact]
    public async Task ReconcileCurrentDevices_IsIdempotent_DoesNotRestartAnAlreadySuccessfulSlot()
    {
        var config = new AppConfig
        {
            DispenseHeadRollbackDriverInfPath = CreateTempInf(),
            PrinterLatestDriverInfPath = CreateTempInf(),
            MaxAutoRetries = 1,
            DriverStepTimeout = TimeSpan.FromSeconds(5),
        };

        var enumerator = new FakeUsbDeviceEnumerator();
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2304\\1", "USB\\VID_067B&PID_2304", "Port_#0002.Hub_#0001", null);
        enumerator.Devices.Add(device);

        var watcher = new DeviceWatcher(enumerator, pollInterval: TimeSpan.FromMilliseconds(50));
        var driverBinder = new FakeDriverBinder();
        var rollbackService = new DriverRollbackService(driverBinder);
        var comPortAssigner = new ComPortAssigner(driverBinder, new FakeComDb());
        var log = new ProvisioningLog(_logPath);

        var coordinator = new ProvisioningCoordinator(config, enumerator, watcher, rollbackService, comPortAssigner, log);
        coordinator.RoleResolver.Learn(device.LocationPath, PortRole.Printer);

        watcher.Poll();
        var status = await WaitForTerminalStatus(coordinator.Slots[PortRole.Printer]);
        Assert.True(status.IsSuccess);
        var callsAfterFirstRun = driverBinder.ForceInstallCalls.Count;

        // Further poll ticks with the same still-connected, already-resolved device
        // must not pile up repeated force-install calls once it's settled into Success.
        watcher.Poll();
        watcher.Poll();
        watcher.Poll();

        Assert.Equal(callsAfterFirstRun, driverBinder.ForceInstallCalls.Count);

        coordinator.Dispose();
    }
}
