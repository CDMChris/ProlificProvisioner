using ProlificProvisioner.Core.ComPorts;
using ProlificProvisioner.Core.Config;
using ProlificProvisioner.Core.Devices;
using ProlificProvisioner.Core.Drivers;
using ProlificProvisioner.Core.Logging;
using ProlificProvisioner.Core.Tests.Fakes;
using ProlificProvisioner.Core.Workflow;
using Xunit;

namespace ProlificProvisioner.Core.Tests;

public class PortSlotControllerTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly string _logPath;

    public PortSlotControllerTests()
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

    private (PortSlotController Controller, FakeDriverBinder DriverBinder, FakeComDb ComDb) BuildController(
        PortRole role, AppConfig? config = null)
    {
        config ??= new AppConfig
        {
            DispenseHeadRollbackDriverInfPath = CreateTempInf(),
            PrinterLatestDriverInfPath = CreateTempInf(),
            MaxAutoRetries = 2,
            DriverStepTimeout = TimeSpan.FromSeconds(5),
        };

        var driverBinder = new FakeDriverBinder();
        var comDb = new FakeComDb();
        var rollbackService = new DriverRollbackService(driverBinder);
        var comPortAssigner = new ComPortAssigner(driverBinder, comDb);
        var log = new ProvisioningLog(_logPath);

        var controller = new PortSlotController(role, config, rollbackService, comPortAssigner, log, () => Array.Empty<UsbSerialDevice>());
        return (controller, driverBinder, comDb);
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
    public async Task DispenseHead_HappyPath_GoesThroughRollbackAndSucceeds()
    {
        var (controller, driverBinder, comDb) = BuildController(PortRole.DispenseHead);
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2303\\1", "USB\\VID_067B&PID_2303", "Port_#0001.Hub_#0001", null);

        controller.OnDeviceArrived(device);
        var status = await WaitForTerminalStatus(controller);

        Assert.True(status.IsSuccess);
        Assert.Contains(comDb.ReservedPorts, p => p == 1);
        Assert.Equal("COM1", comDb.PortNamesByDevice[device.DeviceInstanceId]);
        // Latest driver installed first, then rolled back to known-good — both scoped to this exact device.
        Assert.Equal(2, driverBinder.ForceInstallCalls.Count);
        Assert.All(driverBinder.ForceInstallCalls, c => Assert.Equal(device.DeviceInstanceId, c.DeviceInstanceId));
        Assert.Contains(device.DeviceInstanceId, driverBinder.CyclePowerCalls);
    }

    [Fact]
    public async Task Printer_HappyPath_SkipsRollbackAndSucceeds()
    {
        var (controller, driverBinder, comDb) = BuildController(PortRole.Printer);
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2304\\1", "USB\\VID_067B&PID_2304", "Port_#0002.Hub_#0001", null);

        controller.OnDeviceArrived(device);
        var status = await WaitForTerminalStatus(controller);

        Assert.True(status.IsSuccess);
        Assert.Equal("COM2", comDb.PortNamesByDevice[device.DeviceInstanceId]);
        // Only the single "install latest" step — no rollback for the printer.
        Assert.Single(driverBinder.ForceInstallCalls);
    }

    [Fact]
    public async Task ComPortAssignFailure_ExhaustsRetries_ThenCableFault()
    {
        var config = new AppConfig
        {
            DispenseHeadRollbackDriverInfPath = CreateTempInf(),
            PrinterLatestDriverInfPath = CreateTempInf(),
            MaxAutoRetries = 2,
            DriverStepTimeout = TimeSpan.FromSeconds(5),
        };
        var (controller, _, comDb) = BuildController(PortRole.Printer, config);
        comDb.ThrowOnWrite = true; // simulates a registry write failure, e.g. non-elevated process
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2304\\1", "USB\\VID_067B&PID_2304", "Port_#0002.Hub_#0001", null);

        controller.OnDeviceArrived(device);
        var status = await WaitForTerminalStatus(controller);

        Assert.True(status.IsTerminalFailure);
        Assert.Equal(config.MaxAutoRetries, status.AttemptCount);
        Assert.NotNull(status.FailureReason);
    }

    [Fact]
    public async Task MarkDefective_ResetsSlotToWaiting_AndLogs()
    {
        var config = new AppConfig
        {
            DispenseHeadRollbackDriverInfPath = CreateTempInf(),
            PrinterLatestDriverInfPath = CreateTempInf(),
            MaxAutoRetries = 1,
            DriverStepTimeout = TimeSpan.FromSeconds(5),
        };
        var (controller, _, comDb) = BuildController(PortRole.Printer, config);
        comDb.ThrowOnWrite = true;
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2304\\1", "USB\\VID_067B&PID_2304", "Port_#0002.Hub_#0001", null);

        controller.OnDeviceArrived(device);
        await WaitForTerminalStatus(controller);
        Assert.True(controller.Status.IsTerminalFailure);

        controller.MarkDefective();

        Assert.Equal(ProvisioningStep.WaitingForCable, controller.Status.Step);
        var logContents = await File.ReadAllTextAsync(_logPath);
        Assert.Contains("Marked defective by operator.", logContents);
    }

    [Fact]
    public async Task Retry_AfterFixingUnderlyingIssue_SucceedsOnNextAttempt()
    {
        var config = new AppConfig
        {
            DispenseHeadRollbackDriverInfPath = CreateTempInf(),
            PrinterLatestDriverInfPath = CreateTempInf(),
            MaxAutoRetries = 1, // fail fast into CableFault so we can exercise manual Retry
            DriverStepTimeout = TimeSpan.FromSeconds(5),
        };
        var (controller, _, comDb) = BuildController(PortRole.Printer, config);
        comDb.ThrowOnWrite = true;
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2304\\1", "USB\\VID_067B&PID_2304", "Port_#0002.Hub_#0001", null);

        controller.OnDeviceArrived(device);
        await WaitForTerminalStatus(controller);
        Assert.True(controller.Status.IsTerminalFailure);

        comDb.ThrowOnWrite = false; // "cable reseated" / underlying issue resolved
        controller.Retry();
        var status = await WaitForTerminalStatus(controller);

        Assert.True(status.IsSuccess);
    }

    [Fact]
    public void DeviceRemovedMidProvisioning_ResetsToWaiting()
    {
        var config = new AppConfig
        {
            DispenseHeadRollbackDriverInfPath = CreateTempInf(),
            PrinterLatestDriverInfPath = CreateTempInf(),
            MaxAutoRetries = 1,
            DriverStepTimeout = TimeSpan.FromSeconds(30),
        };
        var (controller, _, _) = BuildController(PortRole.Printer, config);
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2304\\1", "USB\\VID_067B&PID_2304", "Port_#0002.Hub_#0001", null);

        controller.OnDeviceArrived(device);
        controller.OnDeviceRemoved(device);

        Assert.Equal(ProvisioningStep.WaitingForCable, controller.Status.Step);
    }

    [Fact]
    public async Task OnDeviceArrived_CalledRepeatedlyForSameDeviceAfterSuccess_DoesNotReRunSequence()
    {
        // Regression test: ProvisioningCoordinator.ReconcileCurrentDevices calls
        // OnDeviceArrived for every still-connected resolvable device on every poll
        // tick (~1/sec), not just on a genuine physical arrival. A device sitting in
        // Success awaiting "Confirm Labeled" must not get re-provisioned every second.
        var (controller, driverBinder, _) = BuildController(PortRole.Printer);
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2304\\1", "USB\\VID_067B&PID_2304", "Port_#0002.Hub_#0001", null);

        controller.OnDeviceArrived(device);
        await WaitForTerminalStatus(controller);
        Assert.True(controller.Status.IsSuccess);
        var callsAfterFirstRun = driverBinder.ForceInstallCalls.Count;

        controller.OnDeviceArrived(device);
        controller.OnDeviceArrived(device);

        Assert.Equal(callsAfterFirstRun, driverBinder.ForceInstallCalls.Count);
        Assert.True(controller.Status.IsSuccess);
    }

    [Fact]
    public async Task OnDeviceArrived_CalledRepeatedlyWhileCableFault_DoesNotAutoRetry()
    {
        var config = new AppConfig
        {
            DispenseHeadRollbackDriverInfPath = CreateTempInf(),
            PrinterLatestDriverInfPath = CreateTempInf(),
            MaxAutoRetries = 1,
            DriverStepTimeout = TimeSpan.FromSeconds(5),
        };
        var (controller, driverBinder, comDb) = BuildController(PortRole.Printer, config);
        comDb.ThrowOnWrite = true;
        var device = new UsbSerialDevice("USB\\VID_067B&PID_2304\\1", "USB\\VID_067B&PID_2304", "Port_#0002.Hub_#0001", null);

        controller.OnDeviceArrived(device);
        await WaitForTerminalStatus(controller);
        Assert.True(controller.Status.IsTerminalFailure);
        var callsAfterFault = driverBinder.ForceInstallCalls.Count;

        controller.OnDeviceArrived(device);

        Assert.Equal(callsAfterFault, driverBinder.ForceInstallCalls.Count);
        Assert.True(controller.Status.IsTerminalFailure);
    }
}
