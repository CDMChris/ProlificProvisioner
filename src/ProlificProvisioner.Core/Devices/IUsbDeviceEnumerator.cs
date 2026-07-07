namespace ProlificProvisioner.Core.Devices;

/// <summary>
/// Abstraction over "list Prolific USB-serial devices currently plugged in".
/// Real implementation queries WMI; tests supply a fake list.
/// </summary>
public interface IUsbDeviceEnumerator
{
    IReadOnlyList<UsbSerialDevice> EnumerateProlificDevices();
}
