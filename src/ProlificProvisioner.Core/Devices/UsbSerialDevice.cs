namespace ProlificProvisioner.Core.Devices;

/// <summary>
/// A Prolific USB-serial device as enumerated by Windows, before we know which
/// fixture slot (role) it belongs to.
/// </summary>
public sealed record UsbSerialDevice(
    string DeviceInstanceId,
    string HardwareId,
    string LocationPath,
    string? CurrentComPort);
