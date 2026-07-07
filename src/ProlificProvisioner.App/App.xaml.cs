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

        // A single unhandled exception on any thread (a stray registry/driver call,
        // WMI hiccup, etc.) must not silently kill the whole app on an assembly line —
        // that reads as "the app just stopped updating" until someone notices and
        // relaunches it. Log and keep running wherever WPF allows that.
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("UI thread", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("background thread (fatal)", args.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("unobserved task", args.Exception);
            args.SetObserved();
        };

        Config = AppConfig.Load(ConfigPath);
        // Resolve relative driver package / log paths against the install directory
        // so the app works regardless of the working directory it's launched from.
        Config.DispenseHeadRollbackDriverInfPath = ResolveAppRelativePath(Config.DispenseHeadRollbackDriverInfPath);
        Config.PrinterLatestDriverInfPath = ResolveAppRelativePath(Config.PrinterLatestDriverInfPath);
        Config.LogFilePath = ResolveAppRelativePath(Config.LogFilePath);

        var driverBinder = new NativeDriverBinder();
        var rollbackService = new DriverRollbackService(driverBinder);
        var comPortAssigner = new ComPortAssigner(driverBinder);
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

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var path = Path.Combine(AppDataDirectory, "crash-log.txt");
            File.AppendAllText(path, $"{DateTimeOffset.Now:o} [{source}] {ex}\n\n");
        }
        catch
        {
            // If we can't even write the crash log, there's nothing further to do.
        }
    }
}
