using System.Windows;
using ProlificProvisioner.App.ViewModels;
using ProlificProvisioner.Core.Devices;

namespace ProlificProvisioner.App.Views;

public partial class LearnPortsWindow : Window
{
    private readonly LearnPortsViewModel _viewModel;

    public LearnPortsWindow(IUsbDeviceEnumerator enumerator, PortRoleResolver resolver)
    {
        InitializeComponent();
        _viewModel = new LearnPortsViewModel(enumerator, resolver);
        DataContext = _viewModel;
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
        DialogResult = true;
        Close();
    }
}
