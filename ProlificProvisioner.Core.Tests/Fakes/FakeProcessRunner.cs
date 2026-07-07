using ProlificProvisioner.Core.Drivers;

namespace ProlificProvisioner.Core.Tests.Fakes;

/// <summary>Fake IProcessRunner: succeeds for everything by default; specific argument substrings can be configured to fail.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly HashSet<string> _failingArgumentSubstrings = new(StringComparer.OrdinalIgnoreCase);
    public List<(string FileName, string Arguments)> Calls { get; } = new();

    public void FailWhenArgumentsContain(string substring) => _failingArgumentSubstrings.Add(substring);

    public ProcessResult Run(string fileName, string arguments, TimeSpan timeout)
    {
        Calls.Add((fileName, arguments));

        if (_failingArgumentSubstrings.Any(s => arguments.Contains(s, StringComparison.OrdinalIgnoreCase)))
        {
            return new ProcessResult(1, string.Empty, "Simulated failure.");
        }

        return new ProcessResult(0, string.Empty, string.Empty);
    }
}
