using ProlificProvisioner.Core.ComPorts;
using ProlificProvisioner.Core.Config;
using ProlificProvisioner.Core.Devices;
using ProlificProvisioner.Core.Drivers;
using ProlificProvisioner.Core.Logging;

namespace ProlificProvisioner.Core.Workflow;

/// <summary>
/// Drives one fixture slot (dispense head or printer) through its full provisioning
/// sequence in response to device arrival, with per-step timeout, bounded auto-retry,
/// and a terminal Cable Fault state the UI surfaces as "retry or mark defective".
/// </summary>
public sealed class PortSlotController
{
    public PortRole Role { get; }
    public ProvisioningStatus Status { get; private set; }

    public event EventHandler<ProvisioningStatus>? StatusChanged;

    private readonly AppConfig _config;
    private readonly DriverRollbackService _rollbackService;
    private readonly ComPortAssigner _comPortAssigner;
    private readonly ProvisioningLog _log;
    private readonly Func<IReadOnlyList<UsbSerialDevice>> _getCurrentDevices;

    private UsbSerialDevice? _activeDevice;

    public PortSlotController(
        PortRole role,
        AppConfig config,
        DriverRollbackService rollbackService,
        ComPortAssigner comPortAssigner,
        ProvisioningLog log,
        Func<IReadOnlyList<UsbSerialDevice>> getCurrentDevices)
    {
        Role = role;
        Status = ProvisioningStatus.Idle(role);
        _config = config;
        _rollbackService = rollbackService;
        _comPortAssigner = comPortAssigner;
        _log = log;
        _getCurrentDevices = getCurrentDevices;
    }

    /// <summary>
    /// Starts (or, if called again for the same device, is a no-op against) this
    /// slot's provisioning sequence. Called both on genuine physical arrival and, from
    /// <see cref="ProvisioningCoordinator.ReconcileCurrentDevices()"/>, repeatedly for
    /// every still-connected resolvable device on every poll tick — so the same-device
    /// guard below is load-bearing, not just an optimization: without it, a device
    /// sitting in Success (awaiting "Confirm Labeled") or CableFault (awaiting
    /// Retry/Mark Defective) would get its whole sequence silently re-run every second.
    /// </summary>
    public void OnDeviceArrived(UsbSerialDevice device)
    {
        if (_activeDevice?.DeviceInstanceId == device.DeviceInstanceId)
        {
            return;
        }

        if (Status.IsInProgress)
        {
            return;
        }

        _activeDevice = device;
        SetStatus(Status with
        {
            Step = ProvisioningStep.DeviceDetected,
            DeviceInstanceId = device.DeviceInstanceId,
            FailureReason = null,
        });

        _ = RunProvisioningAsync(device);
    }

    public void OnDeviceRemoved(UsbSerialDevice device)
    {
        if (_activeDevice?.DeviceInstanceId != device.DeviceInstanceId || Status.Step == ProvisioningStep.Success)
        {
            return;
        }

        _log.Append(new ProvisioningEvent(DateTimeOffset.Now, Role, device.DeviceInstanceId, Status.Step, false, "Cable unplugged mid-provisioning."));
        _activeDevice = null;
        SetStatus(ProvisioningStatus.Idle(Role));
    }

    /// <summary>Called by the UI once the worker checks "Confirm Labeled" on a successful card.</summary>
    public void ConfirmLabeled()
    {
        if (Status.Step != ProvisioningStep.Success)
        {
            return;
        }

        _activeDevice = null;
        SetStatus(ProvisioningStatus.Idle(Role));
    }

    /// <summary>Re-runs the sequence from a Cable Fault state (e.g. after reseating the cable).</summary>
    public void Retry()
    {
        if (Status.Step != ProvisioningStep.CableFault || _activeDevice is null)
        {
            return;
        }

        var device = _activeDevice;
        SetStatus(Status with { Step = ProvisioningStep.DeviceDetected, FailureReason = null });
        _ = RunProvisioningAsync(device);
    }

    /// <summary>Worker confirms the cable itself is bad; logs it and frees the slot for the next unit.</summary>
    public void MarkDefective()
    {
        if (Status.Step != ProvisioningStep.CableFault)
        {
            return;
        }

        _log.Append(new ProvisioningEvent(
            DateTimeOffset.Now, Role, _activeDevice?.DeviceInstanceId ?? "unknown",
            ProvisioningStep.CableFault, false, "Marked defective by operator."));

        _activeDevice = null;
        SetStatus(ProvisioningStatus.Idle(Role));
    }

    private async Task RunProvisioningAsync(UsbSerialDevice device)
    {
        var attempt = Status.AttemptCount + 1;

        var outcome = await ExecuteSequence(device, attempt);

        // This runs fire-and-forget (nobody awaits RunProvisioningAsync's Task), so if
        // anything below threw uncaught, the status would freeze at whatever the last
        // in-progress step was — forever, since nothing would ever observe the fault.
        // Logging failures specifically must never block a real success/fault from
        // reaching the UI.
        try
        {
            LogOutcome(device, outcome);
        }
        catch
        {
            // Best-effort audit trail; losing a log line must not lose the status update below.
        }

        if (outcome.Success)
        {
            SetStatus(Status with { Step = ProvisioningStep.Success, FailureReason = null, AttemptCount = attempt });
            return;
        }

        if (attempt < _config.MaxAutoRetries)
        {
            SetStatus(Status with { Step = ProvisioningStep.DeviceDetected, FailureReason = outcome.FailureReason, AttemptCount = attempt });
            _ = RunProvisioningAsync(device);
            return;
        }

        SetStatus(Status with { Step = ProvisioningStep.CableFault, FailureReason = outcome.FailureReason, AttemptCount = attempt });
    }

    private void LogOutcome(UsbSerialDevice device, SequenceOutcome outcome)
    {
        if (outcome.Success)
        {
            _log.Append(new ProvisioningEvent(DateTimeOffset.Now, Role, device.DeviceInstanceId, ProvisioningStep.Success, true));
        }
        else
        {
            _log.Append(new ProvisioningEvent(DateTimeOffset.Now, Role, device.DeviceInstanceId, outcome.FailedAtStep, false, outcome.FailureReason));
        }
    }

    private sealed record SequenceOutcome(bool Success, ProvisioningStep FailedAtStep, string? FailureReason);

    private async Task<SequenceOutcome> ExecuteSequence(UsbSerialDevice device, int attempt)
    {
        try
        {
            using var cts = new CancellationTokenSource(_config.DriverStepTimeout);

            SetStatus(Status with { Step = ProvisioningStep.InstallingLatestDriver, AttemptCount = attempt });
            var installResult = await RunStepWithTimeout(
                () => _rollbackService.InstallLatest(device.DeviceInstanceId, _config.PrinterLatestDriverInfPath),
                cts.Token);

            if (!installResult.Success)
            {
                return new SequenceOutcome(false, ProvisioningStep.InstallingLatestDriver, installResult.Detail);
            }

            if (Role.RequiresDriverRollback())
            {
                SetStatus(Status with { Step = ProvisioningStep.RollingBackDriver, AttemptCount = attempt });
                var rollbackResult = await RunStepWithTimeout(
                    () => _rollbackService.RollBackToKnownGood(device.DeviceInstanceId, _config.DispenseHeadRollbackDriverInfPath),
                    cts.Token);

                if (!rollbackResult.Success)
                {
                    return new SequenceOutcome(false, ProvisioningStep.RollingBackDriver, rollbackResult.Detail);
                }
            }

            SetStatus(Status with { Step = ProvisioningStep.AssigningComPort, AttemptCount = attempt });
            var assignResult = await RunStepWithTimeout(
                () => _comPortAssigner.Assign(
                    device.DeviceInstanceId,
                    Role.TargetComPortName(),
                    Role.TargetComNumber(),
                    _getCurrentDevices()),
                cts.Token);

            if (!assignResult.Success)
            {
                return new SequenceOutcome(false, ProvisioningStep.AssigningComPort, assignResult.Detail);
            }

            return new SequenceOutcome(true, ProvisioningStep.Success, null);
        }
        catch (OperationCanceledException)
        {
            return new SequenceOutcome(false, Status.Step, $"Step timed out after {_config.DriverStepTimeout}.");
        }
        catch (Exception ex)
        {
            return new SequenceOutcome(false, Status.Step, ex.Message);
        }
    }

    private static Task<T> RunStepWithTimeout<T>(Func<T> step, CancellationToken token)
        => Task.Run(step, token);

    private void SetStatus(ProvisioningStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
