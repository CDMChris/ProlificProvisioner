using ProlificProvisioner.Core.Config;
using ProlificProvisioner.Core.Workflow;

namespace ProlificProvisioner.Core.Devices;

/// <summary>
/// Maps a physical USB location path to a fixture role (dispense head vs. printer),
/// using the mapping captured by the one-time "Learn Ports" flow. This is what lets
/// the app tell apart two Prolific devices that share an identical hardware ID.
/// </summary>
public sealed class PortRoleResolver
{
    private readonly AppConfig _config;

    public PortRoleResolver(AppConfig config)
    {
        _config = config;
    }

    /// <summary>Returns the role for a device's location path, or null if that physical port hasn't been learned yet.</summary>
    public PortRole? Resolve(string locationPath)
        => _config.PortLocationMap.TryGetValue(locationPath, out var role) ? role : null;

    /// <summary>Records that a physical port location corresponds to the given role. Called by the Learn Ports UI.</summary>
    public void Learn(string locationPath, PortRole role)
    {
        // A location can only ever map to one role; remove any stale mapping for the
        // same role first so re-running Learn Ports doesn't leave two entries pointing
        // at the same role after a fixture rewire.
        var staleKeys = _config.PortLocationMap
            .Where(kv => kv.Value == role && kv.Key != locationPath)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in staleKeys)
        {
            _config.PortLocationMap.Remove(key);
        }

        _config.PortLocationMap[locationPath] = role;
    }

    public bool IsFullyLearned() =>
        _config.PortLocationMap.Values.Contains(PortRole.DispenseHead) &&
        _config.PortLocationMap.Values.Contains(PortRole.Printer);
}
