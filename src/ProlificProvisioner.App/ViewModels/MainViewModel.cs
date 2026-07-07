using System.Windows;
using ProlificProvisioner.Core.Workflow;

namespace ProlificProvisioner.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public PortStatusViewModel DispenseHead { get; }
    public PortStatusViewModel Printer { get; }

    private string? _unresolvedDeviceNotice;
    public string? UnresolvedDeviceNotice
    {
        get => _unresolvedDeviceNotice;
        private set => SetField(ref _unresolvedDeviceNotice, value);
    }

    public RelayCommand DismissUnresolvedNoticeCommand { get; }

    public MainViewModel(ProvisioningCoordinator coordinator)
    {
        DispenseHead = new PortStatusViewModel(coordinator.Slots[PortRole.DispenseHead]);
        Printer = new PortStatusViewModel(coordinator.Slots[PortRole.Printer]);

        DismissUnresolvedNoticeCommand = new RelayCommand(() => UnresolvedDeviceNotice = null);

        coordinator.UnresolvedDeviceDetected += (_, locationPath) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
                UnresolvedDeviceNotice =
                    $"A Prolific cable was plugged into an unrecognized fixture port ({locationPath}). " +
                    "Run Learn Ports from Settings to map it to COM1 or COM2.");
        };
    }
}
