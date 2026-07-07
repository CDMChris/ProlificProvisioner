namespace ProlificProvisioner.Core.Drivers;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Abstraction over launching an external command, so driver/COM logic is unit-testable without spawning real processes.</summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, string arguments, TimeSpan timeout);
}
