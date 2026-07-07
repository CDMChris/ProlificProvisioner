using Microsoft.Win32;
using System.Runtime.Versioning;

namespace ProlificProvisioner.Core.Devices;

/// <summary>
/// Reads/writes the per-device "Device Parameters" registry key under
/// HKLM\SYSTEM\CurrentControlSet\Enum\&lt;deviceInstanceId&gt;\Device Parameters,
/// which is where Windows stores the COM port name (PortName) for a serial device.
/// Shared by device enumeration (read current port) and ComPortAssigner (force a port).
/// </summary>
[SupportedOSPlatform("windows")]
public static class DeviceParametersRegistry
{
    private const string EnumKeyRoot = @"SYSTEM\CurrentControlSet\Enum";

    public static string? ReadCurrentPortName(string deviceInstanceId)
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            $@"{EnumKeyRoot}\{deviceInstanceId}\Device Parameters", writable: false);
        return key?.GetValue("PortName") as string;
    }

    public static void WritePortName(string deviceInstanceId, string portName)
    {
        using var key = Registry.LocalMachine.CreateSubKey(
            $@"{EnumKeyRoot}\{deviceInstanceId}\Device Parameters", writable: true)
            ?? throw new InvalidOperationException(
                $"Could not open/create Device Parameters key for '{deviceInstanceId}'. Is the app running elevated?");
        key.SetValue("PortName", portName, RegistryValueKind.String);
    }
}
