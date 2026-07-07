using System.Management;
using System.Runtime.Versioning;

namespace ProlificProvisioner.Core.Devices;

/// <summary>
/// Enumerates currently-plugged-in Prolific USB-serial devices via WMI (Win32_PnPEntity),
/// filtered to the Prolific vendor ID, and enriches each with its physical location
/// path and current COM port assignment.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiUsbDeviceEnumerator : IUsbDeviceEnumerator
{
    /// <summary>USB-IF vendor ID for Prolific Technology Inc.</summary>
    public const string ProlificVendorId = "VID_067B";

    public IReadOnlyList<UsbSerialDevice> EnumerateProlificDevices()
    {
        var results = new List<UsbSerialDevice>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, PNPDeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID_067B%'");

        foreach (ManagementBaseObject item in searcher.Get())
        {
            using (item)
            {
                var deviceId = (string?)item["PNPDeviceID"] ?? (string?)item["DeviceID"];
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                var locationPath = NativeDeviceLocation.GetLocationInformation(deviceId) ?? "unknown-location";
                var currentComPort = DeviceParametersRegistry.ReadCurrentPortName(deviceId);

                results.Add(new UsbSerialDevice(
                    DeviceInstanceId: deviceId,
                    HardwareId: ExtractHardwareId(deviceId),
                    LocationPath: locationPath,
                    CurrentComPort: currentComPort));
            }
        }

        return results;
    }

    private static string ExtractHardwareId(string deviceInstanceId)
    {
        // e.g. "USB\VID_067B&PID_2303\5&1234ABCD&0&1" -> "USB\VID_067B&PID_2303"
        var lastBackslash = deviceInstanceId.LastIndexOf('\\');
        return lastBackslash > 0 ? deviceInstanceId[..lastBackslash] : deviceInstanceId;
    }
}
