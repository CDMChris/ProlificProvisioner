using System.Text.RegularExpressions;

namespace ProlificProvisioner.Core.Drivers;

public sealed record InstalledDriverInfo(string PublishedName, string OriginalFileName, string Version);

/// <summary>
/// Thin wrapper around pnputil.exe for staging, installing, enumerating, and removing
/// driver packages. Requires the app to run elevated (pnputil driver operations need
/// admin rights).
/// </summary>
public sealed class DriverInstaller
{
    private readonly IProcessRunner _processRunner;
    private readonly TimeSpan _timeout;

    public DriverInstaller(IProcessRunner processRunner, TimeSpan? timeout = null)
    {
        _processRunner = processRunner;
        _timeout = timeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>Stages and installs a driver package (.inf), binding it for all matching hardware IDs currently present.</summary>
    public ProcessResult InstallDriverPackage(string infPath)
    {
        if (!File.Exists(infPath))
        {
            throw new FileNotFoundException($"Driver package not found: {infPath}", infPath);
        }

        return _processRunner.Run("pnputil.exe", $"/add-driver \"{infPath}\" /install", _timeout);
    }

    /// <summary>Forces Windows to (re)install a specific driver package against a specific device instance.</summary>
    public ProcessResult ForceInstallForDevice(string deviceInstanceId)
        => _processRunner.Run("pnputil.exe", $"/scan-devices \"{deviceInstanceId}\"", _timeout);

    /// <summary>Lists driver packages currently in the driver store for the given hardware ID, newest first (pnputil order).</summary>
    public IReadOnlyList<InstalledDriverInfo> EnumerateDriversForHardwareId(string hardwareId)
    {
        var result = _processRunner.Run("pnputil.exe", "/enum-drivers", _timeout);
        return ParseEnumDrivers(result.StandardOutput)
            .Where(d => d.OriginalFileName.Contains(hardwareId, StringComparison.OrdinalIgnoreCase)
                        || result.StandardOutput.Contains(hardwareId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public ProcessResult DeleteDriverPackage(string publishedName, bool force = true)
        => _processRunner.Run("pnputil.exe", $"/delete-driver {publishedName}{(force ? " /force" : string.Empty)}", _timeout);

    public ProcessResult RestartDevice(string deviceInstanceId)
        => _processRunner.Run("pnputil.exe", $"/restart-device \"{deviceInstanceId}\"", _timeout);

    internal static List<InstalledDriverInfo> ParseEnumDrivers(string output)
    {
        // pnputil /enum-drivers output is a series of blocks like:
        //   Published Name:     oem12.inf
        //   Original Name:      prolific.inf
        //   Driver Version:     01/02/2026 4.5.0.0
        var results = new List<InstalledDriverInfo>();
        var blocks = Regex.Split(output, @"(?=Published Name\s*:)");

        foreach (var block in blocks)
        {
            var published = Regex.Match(block, @"Published Name\s*:\s*(\S+)");
            var original = Regex.Match(block, @"Original Name\s*:\s*(\S+)");
            var version = Regex.Match(block, @"Driver Version\s*:\s*(.+)");

            if (published.Success && original.Success)
            {
                results.Add(new InstalledDriverInfo(
                    published.Groups[1].Value,
                    original.Groups[1].Value,
                    version.Success ? version.Groups[1].Value.Trim() : "unknown"));
            }
        }

        return results;
    }
}
