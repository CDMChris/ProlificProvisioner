namespace ProlificProvisioner.Core.Drivers;

/// <summary>
/// Forces a specific driver package onto one specific device instance, and forces a
/// device instance to re-read its configuration. Abstracted so the workflow logic is
/// unit-testable without touching real hardware/SetupAPI.
/// </summary>
public interface IDriverBinder
{
    /// <summary>
    /// Installs <paramref name="infPath"/> against exactly this device instance —
    /// scoped to one devnode, not "every device with this hardware ID". This matters
    /// because both fixture cables share the same Prolific hardware ID; anything that
    /// forces a driver by hardware ID alone (pnputil, devcon update,
    /// UpdateDriverForPlugAndPlayDevices) would affect both connected cables at once.
    /// Throws on failure with a message suitable for surfacing to the operator.
    /// </summary>
    void ForceInstall(string deviceInstanceId, string infPath);

    /// <summary>
    /// Disables then re-enables the device instance, forcing Windows to tear down and
    /// reload its devnode — including re-reading the Device Parameters\PortName
    /// registry value written just before this call. Throws on failure.
    /// </summary>
    void CyclePower(string deviceInstanceId);
}
