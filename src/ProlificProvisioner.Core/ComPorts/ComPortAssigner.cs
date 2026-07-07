using ProlificProvisioner.Core.Devices;
using ProlificProvisioner.Core.Drivers;
using System.Runtime.Versioning;

namespace ProlificProvisioner.Core.ComPorts;

public sealed record ComAssignmentResult(bool Success, string Detail);

/// <summary>
/// Forces a specific COM port number onto a device instance: writes the device's
/// PortName registry value, reserves the number in the ComDB bitmap, and cycles the
/// device (disable/enable) so it re-reads the new port name. Refuses to proceed if
/// another currently-connected device already legitimately holds that port.
/// </summary>
public sealed class ComPortAssigner
{
    private readonly IDriverBinder _driverBinder;
    private readonly IComDb _comDb;

    public ComPortAssigner(IDriverBinder driverBinder, IComDb? comDb = null)
    {
        _driverBinder = driverBinder;
#pragma warning disable CA1416 // RealComDb is only ever exercised at runtime on Windows, by design; tests inject a fake IComDb instead.
        _comDb = comDb ?? new RealComDb();
#pragma warning restore CA1416
    }

    public ComAssignmentResult Assign(
        string deviceInstanceId,
        string targetPortName,
        int targetComNumber,
        IReadOnlyList<UsbSerialDevice> currentlyConnectedDevices)
    {
        var collidingDevice = currentlyConnectedDevices.FirstOrDefault(d =>
            d.DeviceInstanceId != deviceInstanceId &&
            string.Equals(d.CurrentComPort, targetPortName, StringComparison.OrdinalIgnoreCase));

        if (collidingDevice is not null)
        {
            return new ComAssignmentResult(false,
                $"{targetPortName} is already in use by another connected device ({collidingDevice.DeviceInstanceId}).");
        }

        try
        {
            // Stale reservations from a previously-provisioned, now-unplugged cable
            // shouldn't block reuse of the number on this fixture.
            _comDb.Release(targetComNumber);
            _comDb.WritePortName(deviceInstanceId, targetPortName);
            _comDb.Reserve(targetComNumber);
        }
        catch (Exception ex)
        {
            return new ComAssignmentResult(false, $"Failed to write COM port assignment: {ex.Message}");
        }

        try
        {
            _driverBinder.CyclePower(deviceInstanceId);
        }
        catch (Exception ex)
        {
            return new ComAssignmentResult(false, $"Port assigned but device didn't re-enumerate: {ex.Message}");
        }

        return new ComAssignmentResult(true, $"Assigned {targetPortName}.");
    }
}

/// <summary>Seam over the ComDB + device-parameters registry writes, so ComPortAssigner is unit-testable without touching the real registry.</summary>
public interface IComDb
{
    void Reserve(int comNumber);
    void Release(int comNumber);
    void WritePortName(string deviceInstanceId, string portName);
}

[SupportedOSPlatform("windows")]
public sealed class RealComDb : IComDb
{
    public void Reserve(int comNumber) => ComDbReservation.Reserve(comNumber);
    public void Release(int comNumber) => ComDbReservation.Release(comNumber);
    public void WritePortName(string deviceInstanceId, string portName) => DeviceParametersRegistry.WritePortName(deviceInstanceId, portName);
}
