using System.Collections.ObjectModel;
using System.Windows.Threading;
using ProlificProvisioner.Core.Devices;
using ProlificProvisioner.Core.Workflow;

namespace ProlificProvisioner.App.ViewModels;

public sealed class DetectedDeviceRow : ObservableObject
{
    public UsbSerialDevice Device { get; }

    public string LocationPath => Device.LocationPath;

    private string? _currentRole;
    public string? CurrentRole
    {
        get => _currentRole;
        set => SetField(ref _currentRole, value);
    }

    public RelayCommand AssignDispenseHeadCommand { get; }
    public RelayCommand AssignPrinterCommand { get; }

    public DetectedDeviceRow(UsbSerialDevice device, PortRoleResolver resolver, Action onAssigned)
    {
        Device = device;
        CurrentRole = resolver.Resolve(device.LocationPath)?.ToString();

        AssignDispenseHeadCommand = new RelayCommand(() =>
        {
            resolver.Learn(device.LocationPath, PortRole.DispenseHead);
            CurrentRole = PortRole.DispenseHead.ToString();
            onAssigned();
        });

        AssignPrinterCommand = new RelayCommand(() =>
        {
            resolver.Learn(device.LocationPath, PortRole.Printer);
            CurrentRole = PortRole.Printer.ToString();
            onAssigned();
        });
    }
}

/// <summary>
/// Drives the one-time (or re-run-on-rewire) fixture setup: shows every Prolific
/// device currently plugged in, live, so a tech can plug a cable into each fixture
/// slot in turn and tag it as Dispense Head or Printer.
/// </summary>
public sealed class LearnPortsViewModel : ObservableObject, IDisposable
{
    private readonly IUsbDeviceEnumerator _enumerator;
    private readonly PortRoleResolver _resolver;
    private readonly DispatcherTimer _timer;

    public ObservableCollection<DetectedDeviceRow> DetectedDevices { get; } = new();

    public LearnPortsViewModel(IUsbDeviceEnumerator enumerator, PortRoleResolver resolver)
    {
        _enumerator = enumerator;
        _resolver = resolver;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    private void Refresh()
    {
        var current = _enumerator.EnumerateProlificDevices();

        var currentIds = current.Select(d => d.DeviceInstanceId).ToHashSet();
        var staleRows = DetectedDevices.Where(r => !currentIds.Contains(r.Device.DeviceInstanceId)).ToList();
        foreach (var row in staleRows)
        {
            DetectedDevices.Remove(row);
        }

        var knownIds = DetectedDevices.Select(r => r.Device.DeviceInstanceId).ToHashSet();
        foreach (var device in current.Where(d => !knownIds.Contains(d.DeviceInstanceId)))
        {
            DetectedDevices.Add(new DetectedDeviceRow(device, _resolver, RaiseAssignmentChanged));
        }
    }

    private void RaiseAssignmentChanged() => RaisePropertyChanged(nameof(IsFullyLearned));

    public bool IsFullyLearned => _resolver.IsFullyLearned();

    public void Dispose() => _timer.Stop();
}
