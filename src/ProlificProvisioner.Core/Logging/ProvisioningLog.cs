using System.Globalization;
using ProlificProvisioner.Core.Workflow;

namespace ProlificProvisioner.Core.Logging;

/// <summary>Append-only CSV log of every provisioning attempt, for line QA traceability (cable faults, timings, driver versions used).</summary>
public sealed class ProvisioningLog
{
    private readonly string _path;
    private readonly object _lock = new();

    public ProvisioningLog(string path)
    {
        _path = path;
        EnsureHeader();
    }

    private void EnsureHeader()
    {
        if (!File.Exists(_path))
        {
            File.WriteAllText(_path, "Timestamp,Role,DeviceInstanceId,Step,Success,Detail\n");
        }
    }

    public void Append(ProvisioningEvent evt)
    {
        var line = string.Join(",",
            evt.Timestamp.ToString("o", CultureInfo.InvariantCulture),
            evt.Role,
            Escape(evt.DeviceInstanceId),
            evt.Step,
            evt.Success,
            Escape(evt.Detail ?? string.Empty));

        lock (_lock)
        {
            File.AppendAllText(_path, line + "\n");
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
