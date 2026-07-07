namespace ProlificProvisioner.Core.Drivers;

public sealed record DriverStepResult(bool Success, string Detail);

/// <summary>
/// Drives the driver side of provisioning for both fixture slots via
/// <see cref="IDriverBinder"/>, which forces a specific bundled .inf onto exactly one
/// device instance — matching the operator's manual "Update Driver → pick a specific
/// model → Next" flow in Device Manager.
///
/// For the dispense-head port, "download latest, then Roll Back Driver" is reproduced
/// deterministically rather than literally: Windows has no supported API for the
/// literal Roll Back Driver button (it's an internal, undocumented Device Manager
/// action, and its outcome depends on whatever happened to be installed before — not
/// reproducible on demand). Instead this force-installs the bundled "latest" package
/// first, then force-installs the bundled known-good package — landing on the same
/// known-good end state every time, regardless of the device's install history.
/// </summary>
public sealed class DriverRollbackService
{
    private readonly IDriverBinder _binder;

    public DriverRollbackService(IDriverBinder binder)
    {
        _binder = binder;
    }

    public DriverStepResult InstallLatest(string deviceInstanceId, string latestInfPath)
    {
        try
        {
            _binder.ForceInstall(deviceInstanceId, latestInfPath);
            return new DriverStepResult(true, "Latest driver installed.");
        }
        catch (Exception ex)
        {
            return new DriverStepResult(false, $"Failed to install latest driver: {ex.Message}");
        }
    }

    public DriverStepResult RollBackToKnownGood(string deviceInstanceId, string knownGoodInfPath)
    {
        try
        {
            _binder.ForceInstall(deviceInstanceId, knownGoodInfPath);
            return new DriverStepResult(true, "Rolled back to known-good driver.");
        }
        catch (Exception ex)
        {
            return new DriverStepResult(false, $"Failed to roll back to known-good driver: {ex.Message}");
        }
    }
}
