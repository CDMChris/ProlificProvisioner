using ProlificProvisioner.Core.Drivers;

namespace ProlificProvisioner.Core.Tests.Fakes;

/// <summary>Fake IDriverBinder: records calls in-memory instead of touching real SetupAPI/hardware.</summary>
public sealed class FakeDriverBinder : IDriverBinder
{
    public List<(string DeviceInstanceId, string InfPath)> ForceInstallCalls { get; } = new();
    public List<string> CyclePowerCalls { get; } = new();
    public bool ThrowOnForceInstall { get; set; }
    public bool ThrowOnCyclePower { get; set; }

    public void ForceInstall(string deviceInstanceId, string infPath)
    {
        ForceInstallCalls.Add((deviceInstanceId, infPath));
        if (ThrowOnForceInstall)
        {
            throw new InvalidOperationException("Simulated driver install failure.");
        }
    }

    public void CyclePower(string deviceInstanceId)
    {
        CyclePowerCalls.Add(deviceInstanceId);
        if (ThrowOnCyclePower)
        {
            throw new InvalidOperationException("Simulated device cycle failure.");
        }
    }
}
