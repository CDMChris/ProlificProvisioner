using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ProlificProvisioner.Core.Drivers;

/// <summary>
/// SetupAPI-backed <see cref="IDriverBinder"/>. Uses the same underlying mechanism as
/// Device Manager's per-device "Update Driver → Let me pick from a list of available
/// drivers on my computer → select a specific model → Next" flow (SetupDiInstallDevice
/// against one device's SP_DEVINFO_DATA with a single forced INF), and the "Disable
/// device" / "Enable device" context-menu actions (DIF_PROPERTYCHANGE).
///
/// This is deliberately NOT UpdateDriverForPlugAndPlayDevicesW/devcon-update/pnputil —
/// those all force a driver by hardware ID, which would hit every currently-connected
/// device sharing that ID. Since both fixture cables report the same Prolific hardware
/// ID, that would cross-contaminate the dispense-head and printer driver versions
/// whenever both are plugged in at once. Everything here is scoped to one exact
/// device instance ID instead, matching what the operator does by hand in the tree.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NativeDriverBinder : IDriverBinder
{
    public void ForceInstall(string deviceInstanceId, string infPath)
    {
        if (!File.Exists(infPath))
        {
            throw new FileNotFoundException($"Driver .inf not found: {infPath}", infPath);
        }

        var deviceInfoSet = Native.SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero,
            Native.DIGCF_ALLCLASSES | Native.DIGCF_PRESENT);
        if (deviceInfoSet == Native.InvalidHandle)
        {
            throw Win32("SetupDiGetClassDevs failed while locating the device.");
        }

        try
        {
            var deviceInfoData = FindDeviceInfoData(deviceInfoSet, deviceInstanceId)
                ?? throw new InvalidOperationException($"Device '{deviceInstanceId}' is not currently present.");

            var installParams = new Native.SP_DEVINSTALL_PARAMS
            {
                cbSize = Marshal.SizeOf<Native.SP_DEVINSTALL_PARAMS>(),
            };
            if (!Native.SetupDiGetDeviceInstallParamsW(deviceInfoSet, ref deviceInfoData, ref installParams))
            {
                throw Win32("Failed to read device install params.");
            }

            installParams.Flags |= Native.DI_ENUMSINGLEINF;
            installParams.DriverPath = infPath;
            if (!Native.SetupDiSetDeviceInstallParamsW(deviceInfoSet, ref deviceInfoData, ref installParams))
            {
                throw Win32($"Failed to point device install at '{infPath}'.");
            }

            if (!Native.SetupDiBuildDriverInfoList(deviceInfoSet, ref deviceInfoData, Native.SPDIT_COMPATDRIVER))
            {
                throw Win32("Failed to build driver info list from the .inf.");
            }

            try
            {
                var driverInfoData = new Native.SP_DRVINFO_DATA_V2
                {
                    cbSize = Marshal.SizeOf<Native.SP_DRVINFO_DATA_V2>(),
                };
                if (!Native.SetupDiEnumDriverInfoW(deviceInfoSet, ref deviceInfoData, Native.SPDIT_COMPATDRIVER, 0, ref driverInfoData))
                {
                    throw Win32($"'{infPath}' has no driver compatible with this device's hardware ID.");
                }

                if (!Native.SetupDiSetSelectedDriverW(deviceInfoSet, ref deviceInfoData, ref driverInfoData))
                {
                    throw Win32("Failed to select the driver for install.");
                }

                if (!Native.SetupDiCallClassInstaller(Native.DIF_INSTALLDEVICE, deviceInfoSet, ref deviceInfoData))
                {
                    throw Win32("Driver install failed (DIF_INSTALLDEVICE).");
                }
            }
            finally
            {
                Native.SetupDiDestroyDriverInfoList(deviceInfoSet, ref deviceInfoData, Native.SPDIT_COMPATDRIVER);
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    public void CyclePower(string deviceInstanceId)
    {
        var deviceInfoSet = Native.SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero,
            Native.DIGCF_ALLCLASSES | Native.DIGCF_PRESENT);
        if (deviceInfoSet == Native.InvalidHandle)
        {
            throw Win32("SetupDiGetClassDevs failed while locating the device.");
        }

        try
        {
            var deviceInfoData = FindDeviceInfoData(deviceInfoSet, deviceInstanceId)
                ?? throw new InvalidOperationException($"Device '{deviceInstanceId}' is not currently present.");

            SetDeviceState(deviceInfoSet, ref deviceInfoData, Native.DICS_DISABLE);
            SetDeviceState(deviceInfoSet, ref deviceInfoData, Native.DICS_ENABLE);
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static void SetDeviceState(IntPtr deviceInfoSet, ref Native.SP_DEVINFO_DATA deviceInfoData, uint stateChange)
    {
        var propChangeParams = new Native.SP_PROPCHANGE_PARAMS
        {
            ClassInstallHeader = new Native.SP_CLASSINSTALL_HEADER
            {
                cbSize = Marshal.SizeOf<Native.SP_CLASSINSTALL_HEADER>(),
                InstallFunction = Native.DIF_PROPERTYCHANGE,
            },
            StateChange = stateChange,
            Scope = Native.DICS_FLAG_GLOBAL,
            HwProfile = 0,
        };

        if (!Native.SetupDiSetClassInstallParamsW(deviceInfoSet, ref deviceInfoData, ref propChangeParams,
                Marshal.SizeOf<Native.SP_PROPCHANGE_PARAMS>()))
        {
            throw Win32($"Failed to stage device state change ({stateChange}).");
        }

        if (!Native.SetupDiCallClassInstaller(Native.DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData))
        {
            throw Win32($"Failed to apply device state change ({stateChange}).");
        }
    }

    private static Native.SP_DEVINFO_DATA? FindDeviceInfoData(IntPtr deviceInfoSet, string deviceInstanceId)
    {
        for (uint index = 0; ; index++)
        {
            var data = new Native.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVINFO_DATA>() };
            if (!Native.SetupDiEnumDeviceInfo(deviceInfoSet, index, ref data))
            {
                return null; // enumeration exhausted
            }

            var buffer = new StringBuilder(512);
            if (Native.SetupDiGetDeviceInstanceIdW(deviceInfoSet, ref data, buffer, buffer.Capacity, out _)
                && string.Equals(buffer.ToString(), deviceInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return data;
            }
        }
    }

    private static Win32Exception Win32(string message)
        => new(Marshal.GetLastWin32Error(), message);

    /// <summary>P/Invoke declarations. All struct layouts/constants are the stable, publicly documented SetupAPI ABI (unchanged since Windows XP).</summary>
    private static class Native
    {
        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint DIGCF_ALLCLASSES = 0x00000004;
        public const int DI_ENUMSINGLEINF = 0x00010000;
        public const uint SPDIT_COMPATDRIVER = 0x00000002;
        public const uint DIF_INSTALLDEVICE = 0x00000002;
        public const uint DIF_PROPERTYCHANGE = 0x00000012;
        public const uint DICS_ENABLE = 0x00000001;
        public const uint DICS_DISABLE = 0x00000002;
        public const uint DICS_FLAG_GLOBAL = 0x00000001;
        public static readonly IntPtr InvalidHandle = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SP_DEVINSTALL_PARAMS
        {
            public int cbSize;
            public int Flags;
            public int FlagsEx;
            public IntPtr hwndParent;
            public IntPtr InstallMsgHandler;
            public IntPtr InstallMsgHandlerContext;
            public IntPtr FileQueue;
            public IntPtr ClassInstallReserved;
            public int Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string DriverPath;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SP_DRVINFO_DATA_V2
        {
            public int cbSize;
            public int DriverType;
            public IntPtr Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Description;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string MfgName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ProviderName;
            public long DriverDate;
            public ulong DriverVersion;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_CLASSINSTALL_HEADER
        {
            public int cbSize;
            public uint InstallFunction;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_PROPCHANGE_PARAMS
        {
            public SP_CLASSINSTALL_HEADER ClassInstallHeader;
            public uint StateChange;
            public uint Scope;
            public uint HwProfile;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInstanceIdW(
            IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInstallParamsW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref SP_DEVINSTALL_PARAMS deviceInstallParams);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiSetDeviceInstallParamsW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref SP_DEVINSTALL_PARAMS deviceInstallParams);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiBuildDriverInfoList(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint driverType);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiEnumDriverInfoW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint driverType, uint memberIndex, ref SP_DRVINFO_DATA_V2 driverInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiSetSelectedDriverW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref SP_DRVINFO_DATA_V2 driverInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDriverInfoList(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint driverType);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiCallClassInstaller(uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiSetClassInstallParamsW(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    }
}
