using System.Text.Json;
using ProlificProvisioner.Core.Workflow;

namespace ProlificProvisioner.Core.Config;

public sealed class AppConfig
{
    /// <summary>Physical USB location path (from NativeDeviceLocation) -> fixture role. Populated by Learn Ports.</summary>
    public Dictionary<string, PortRole> PortLocationMap { get; set; } = new();

    public string DispenseHeadRollbackDriverInfPath { get; set; } = "Drivers/DispenseHead-Rollback/plser_1.inf";

    public string PrinterLatestDriverInfPath { get; set; } = "Drivers/Printer-2026/plser_1.inf";

    public TimeSpan DeviceDetectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan DriverStepTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public int MaxAutoRetries { get; set; } = 2;

    public string LogFilePath { get; set; } = "provisioning-log.csv";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }
}
