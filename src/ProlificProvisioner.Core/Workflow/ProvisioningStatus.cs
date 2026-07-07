namespace ProlificProvisioner.Core.Workflow;

public enum ProvisioningStep
{
    WaitingForCable,
    DeviceDetected,
    InstallingLatestDriver,
    RollingBackDriver,
    AssigningComPort,
    Success,
    CableFault,
}

/// <summary>Immutable snapshot of a port slot's current state, for UI binding.</summary>
public sealed record ProvisioningStatus(
    PortRole Role,
    ProvisioningStep Step,
    string? DeviceInstanceId = null,
    string? FailureReason = null,
    int AttemptCount = 0)
{
    public static ProvisioningStatus Idle(PortRole role) => new(role, ProvisioningStep.WaitingForCable);

    public bool IsTerminalFailure => Step == ProvisioningStep.CableFault;
    public bool IsSuccess => Step == ProvisioningStep.Success;
    public bool IsInProgress => Step is ProvisioningStep.DeviceDetected
        or ProvisioningStep.InstallingLatestDriver
        or ProvisioningStep.RollingBackDriver
        or ProvisioningStep.AssigningComPort;
}
