using System.IO;
using System.Windows;
using ProlificProvisioner.Core.ComPorts;
using ProlificProvisioner.Core.Config;
using ProlificProvisioner.Core.Devices;
using ProlificProvisioner.Core.Drivers;
using ProlificProvisioner.Core.Logging;
using ProlificProvisioner.Core.Workflow;

namespace ProlificProvisioner.App;

public partial class App : Application
{
    /// <summary>Folder next to the executable holding config.json, the bundled driver packages, and the provisioning log.</summary>
    public static string AppDataDirectory { get; } = AppContext.BaseDirectory;

    public ProvisioningCoordinator Coordinator { get; private set; } = null!;
    public AppConfig Config { get; private set; } = null!;
    public IUsbDeviceEnumerator Enumerator { get; private set; } = null!;

    private static string ConfigPath => Path.Combine(AppDataDirectory, "config.json");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Config = AppConfig.Load(ConfigPath);
        // Resolve relative driver package / log paths against the install directory
        // so the app works regardless of the working directory it's launched from.
        Config.DispenseHeadRollbackDriverInfPath = ResolveAppRelativePath(Config.DispenseHeadRollbackDriverInfPath);
        Config.PrinterLatestDriverInfPath = ResolveAppRelativePath(Config.PrinterLatestDriverInfPath);
        Config.LogFilePath = ResolveAppRelativePath(Config.LogFilePath);

        var processRunner = new SystemProcessRunner();
        var driverInstaller = new DriverInstaller(processRunner, Config.DriverStepTimeout);
        var rollbackService = new DriverRollbackService(driverInstaller);
        var comPortAssigner = new ComPortAssigner(driverInstaller);
        var log = new ProvisioningLog(Config.LogFilePath);
        Enumerator = new WmiUsbDeviceEnumerator();
        var watcher = new DeviceWatcher(Enumerator);

        Coordinator = new ProvisioningCoordinator(Config, Enumerator, watcher, rollbackService, comPortAssigner, log);
        Coordinator.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Coordinator.Stop();
        Coordinator.Dispose();
        Config.Save(ConfigPath);
        base.OnExit(e);
    }

    private static string ResolveAppRelativePath(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(AppDataDirectory, path);
}
