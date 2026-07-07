namespace ProlificProvisioner.Core.Workflow;

/// <summary>The two provisioning slots on the fixture.</summary>
public enum PortRole
{
    DispenseHead,
    Printer,
}

public static class PortRoleExtensions
{
    public static int TargetComNumber(this PortRole role) => role switch
    {
        PortRole.DispenseHead => 1,
        PortRole.Printer => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static string TargetComPortName(this PortRole role) => $"COM{role.TargetComNumber()}";

    public static string CableLabel(this PortRole role) => role switch
    {
        PortRole.DispenseHead => "COM1 - Dispense Head",
        PortRole.Printer => "COM2 - Printer",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static bool RequiresDriverRollback(this PortRole role) => role == PortRole.DispenseHead;
}
