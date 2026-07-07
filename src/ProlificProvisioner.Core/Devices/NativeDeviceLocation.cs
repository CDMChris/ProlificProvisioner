using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ProlificProvisioner.Core.Devices;

/// <summary>
/// Thin CfgMgr32 P/Invoke wrapper to read a device's physical "Location Information"
/// string (the same value Device Manager shows under Properties &gt; Location) for a
/// given device instance ID. This is what distinguishes two Prolific cables plugged
/// into different physical hub ports on the fixture, since they share the same VID/PID.
///
/// DllImport declarations resolve lazily at call time, so this compiles fine on
/// non-Windows hosts; it only throws (DllNotFoundException / EntryPointNotFoundException)
/// if actually invoked off Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NativeDeviceLocation
{
    private const int CR_SUCCESS = 0;
    private const uint CM_LOCATE_DEVNODE_NORMAL = 0;
    private const uint CM_DRP_LOCATION_INFORMATION = 0x0E;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(
        uint dnDevInst, uint ulProperty, out uint pulRegDataType, byte[]? buffer, ref uint pulLength, uint ulFlags);

    /// <summary>
    /// Returns the stable "location information" string for the device instance
    /// (e.g. "Port_#0002.Hub_#0003"), or null if it can't be resolved (device
    /// unplugged mid-call, or the platform doesn't support the property).
    /// </summary>
    public static string? GetLocationInformation(string deviceInstanceId)
    {
        var cr = CM_Locate_DevNodeW(out var devInst, deviceInstanceId, CM_LOCATE_DEVNODE_NORMAL);
        if (cr != CR_SUCCESS)
        {
            return null;
        }

        uint length = 512;
        var buffer = new byte[length];
        cr = CM_Get_DevNode_Registry_PropertyW(devInst, CM_DRP_LOCATION_INFORMATION, out _, buffer, ref length, 0);
        if (cr != CR_SUCCESS || length == 0)
        {
            return null;
        }

        return System.Text.Encoding.Unicode.GetString(buffer, 0, (int)length).TrimEnd('\0');
    }
}
