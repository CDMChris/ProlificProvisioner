using System.Windows;
using System.Windows.Media;
using ProlificProvisioner.Core.Workflow;

namespace ProlificProvisioner.App.ViewModels;

/// <summary>Binds one <see cref="PortSlotController"/> to the dashboard card for that fixture slot.</summary>
public sealed class PortStatusViewModel : ObservableObject
{
    private readonly PortSlotController _controller;

    public string RoleTitle => _controller.Role == PortRole.DispenseHead ? "Dispense Head" : "Printer";
    public string TargetPort => _controller.Role.TargetComPortName();

    private ProvisioningStatus _status;
    public ProvisioningStatus Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string StepText => Status.Step switch
    {
        ProvisioningStep.WaitingForCable => "Waiting for cable…",
        ProvisioningStep.DeviceDetected => "Cable detected",
        ProvisioningStep.InstallingLatestDriver => "Installing latest driver…",
        ProvisioningStep.RollingBackDriver => "Rolling back to known-good driver…",
        ProvisioningStep.AssigningComPort => $"Assigning {TargetPort}…",
        ProvisioningStep.Success => $"Ready — label as \"{_controller.Role.CableLabel()}\"",
        ProvisioningStep.CableFault => "Cable fault — retry or mark defective",
        _ => string.Empty,
    };

    public string LabelText => _controller.Role.CableLabel();

    public string? FailureReason => Status.FailureReason;

    public bool IsWaiting => Status.Step == ProvisioningStep.WaitingForCable;
    public bool IsInProgress => Status.IsInProgress;
    public bool IsSuccess => Status.IsSuccess;
    public bool IsFault => Status.IsTerminalFailure;

    public Brush StatusBrush => Status.Step switch
    {
        ProvisioningStep.Success => (Brush)Application.Current.Resources["StatusSuccessBrush"],
        ProvisioningStep.CableFault => (Brush)Application.Current.Resources["StatusFaultBrush"],
        ProvisioningStep.WaitingForCable => (Brush)Application.Current.Resources["StatusIdleBrush"],
        _ => (Brush)Application.Current.Resources["StatusInProgressBrush"],
    };

    public RelayCommand ConfirmLabeledCommand { get; }
    public RelayCommand RetryCommand { get; }
    public RelayCommand MarkDefectiveCommand { get; }

    public PortStatusViewModel(PortSlotController controller)
    {
        _controller = controller;
        _status = controller.Status;

        ConfirmLabeledCommand = new RelayCommand(_controller.ConfirmLabeled, () => IsSuccess);
        RetryCommand = new RelayCommand(_controller.Retry, () => IsFault);
        MarkDefectiveCommand = new RelayCommand(_controller.MarkDefective, () => IsFault);

        _controller.StatusChanged += OnControllerStatusChanged;
    }

    private void OnControllerStatusChanged(object? sender, ProvisioningStatus status)
    {
        // PortSlotController raises this from background Task.Run continuations; all
        // bound UI properties must update on the dispatcher thread.
        Application.Current.Dispatcher.Invoke(() =>
        {
            Status = status;
            RaisePropertyChanged(nameof(StepText));
            RaisePropertyChanged(nameof(FailureReason));
            RaisePropertyChanged(nameof(IsWaiting));
            RaisePropertyChanged(nameof(IsInProgress));
            RaisePropertyChanged(nameof(IsSuccess));
            RaisePropertyChanged(nameof(IsFault));
            RaisePropertyChanged(nameof(StatusBrush));
            ConfirmLabeledCommand.RaiseCanExecuteChanged();
            RetryCommand.RaiseCanExecuteChanged();
            MarkDefectiveCommand.RaiseCanExecuteChanged();
        });
    }
}
