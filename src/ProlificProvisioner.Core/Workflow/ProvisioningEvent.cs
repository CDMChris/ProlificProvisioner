namespace ProlificProvisioner.Core.Workflow;

/// <summary>A single logged occurrence, written to the provisioning log for line QA traceability.</summary>
public sealed record ProvisioningEvent(
    DateTimeOffset Timestamp,
    PortRole Role,
    string DeviceInstanceId,
    ProvisioningStep Step,
    bool Success,
    string? Detail = null);
