using System.Globalization;
using System.Windows;
using ProlificProvisioner.Core.Config;
using ProlificProvisioner.Core.Devices;

namespace ProlificProvisioner.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly PortRoleResolver _resolver;

    public SettingsWindow(AppConfig config, PortRoleResolver resolver)
    {
        InitializeComponent();
        _config = config;
        _resolver = resolver;

        DispenseHeadInfBox.Text = _config.DispenseHeadRollbackDriverInfPath;
        PrinterInfBox.Text = _config.PrinterLatestDriverInfPath;
        TimeoutBox.Text = _config.DriverStepTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture);
        MaxRetriesBox.Text = _config.MaxAutoRetries.ToString(CultureInfo.InvariantCulture);
    }

    private void LearnPorts_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var learnPorts = new LearnPortsWindow(app.Enumerator, _resolver) { Owner = this };
        learnPorts.ShowDialog();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TimeoutBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var timeoutSeconds) || timeoutSeconds <= 0)
        {
            MessageBox.Show(this, "Timeout must be a positive number of seconds.", "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(MaxRetriesBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxRetries) || maxRetries < 1)
        {
            MessageBox.Show(this, "Max retries must be a positive whole number.", "Invalid value", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _config.DispenseHeadRollbackDriverInfPath = DispenseHeadInfBox.Text.Trim();
        _config.PrinterLatestDriverInfPath = PrinterInfBox.Text.Trim();
        _config.DriverStepTimeout = TimeSpan.FromSeconds(timeoutSeconds);
        _config.MaxAutoRetries = maxRetries;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
