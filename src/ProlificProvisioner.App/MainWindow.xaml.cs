using System.Windows;
using ProlificProvisioner.App.ViewModels;
using ProlificProvisioner.App.Views;

namespace ProlificProvisioner.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(((App)Application.Current).Coordinator);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var settings = new SettingsWindow(app.Config, app.Coordinator.RoleResolver)
        {
            Owner = this,
        };
        settings.ShowDialog();
    }
}
