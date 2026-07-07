namespace ProlificProvisioner.Core.Drivers;

public sealed record DriverStepResult(bool Success, string Detail);

/// <summary>
/// Reproduces the outcome of "install latest driver, then Roll Back Driver" for the
/// dispense-head port. Windows has no supported API/CLI for the literal Roll Back
/// Driver button (it's an internal Device Manager action), so this instead:
///   1. Installs the latest driver package (matches the manual "download latest" step).
///   2. Removes that driver package for the device.
///   3. Force-installs the bundled known-good older INF against the device's hardware ID.
/// The end state matches what the tech gets today by clicking Roll Back, but it's
/// deterministic and scriptable instead of depending on Device Manager's rollback history.
/// </summary>
public sealed class DriverRollbackService
{
    private readonly DriverInstaller _installer;

    public DriverRollbackService(DriverInstaller installer)
    {
        _installer = installer;
    }

    /// <summary>
    /// For the dispense-head port: "latest driver" means whatever Windows Update currently
    /// offers, which changes over time — there's no fixed file to bundle for this step.
    /// Triggers Windows' online driver search against the device (pnputil /scan-devices),
    /// mirroring the manual "download latest driver" step. Its result is superseded by
    /// <see cref="RollBackToKnownGood"/> immediately after, so a failure here (e.g. no
    /// network) is non-fatal — logged, but the rollback step still proceeds.
    /// </summary>
    public DriverStepResult InstallLatestViaWindowsUpdate(string deviceInstanceId)
    {
        var result = _installer.ForceInstallForDevice(deviceInstanceId);
        return result.Succeeded
            ? new DriverStepResult(true, "Latest driver installed via Windows Update.")
            : new DriverStepResult(false, $"Windows Update driver search failed (non-fatal, continuing to rollback): {result.StandardError.Trim()}");
    }

    /// <summary>For the printer port: a fixed, bundled driver package (the 2026 release) is always used — install it directly.</summary>
    public DriverStepResult InstallBundledDriver(string infPath)
    {
        var result = _installer.InstallDriverPackage(infPath);
        return result.Succeeded
            ? new DriverStepResult(true, "Driver installed.")
            : new DriverStepResult(false, $"Failed to install driver: {result.StandardError.Trim()}");
    }

    /// <summary>Executes steps 2 and 3 above for the dispense-head device.</summary>
    public DriverStepResult RollBackToKnownGood(string deviceInstanceId, string hardwareId, string knownGoodInfPath)
    {
        var installed = _installer.EnumerateDriversForHardwareId(hardwareId);
        foreach (var driver in installed)
        {
            var deleteResult = _installer.DeleteDriverPackage(driver.PublishedName, force: true);
            if (!deleteResult.Succeeded)
            {
                return new DriverStepResult(false,
                    $"Failed to remove driver package {driver.PublishedName}: {deleteResult.StandardError.Trim()}");
            }
        }

        var installResult = _installer.InstallDriverPackage(knownGoodInfPath);
        if (!installResult.Succeeded)
        {
            return new DriverStepResult(false, $"Failed to install known-good driver: {installResult.StandardError.Trim()}");
        }

        var restartResult = _installer.RestartDevice(deviceInstanceId);
        if (!restartResult.Succeeded)
        {
            return new DriverStepResult(false, $"Driver installed but device restart failed: {restartResult.StandardError.Trim()}");
        }

        return new DriverStepResult(true, "Rolled back to known-good driver.");
    }
}
