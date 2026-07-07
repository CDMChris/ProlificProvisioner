using System.Diagnostics;
using System.Text;

namespace ProlificProvisioner.Core.Drivers;

/// <summary>Real IProcessRunner backed by System.Diagnostics.Process. Used for pnputil.exe invocations.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    public ProcessResult Run(string fileName, string arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new ProcessResult(-1, stdout.ToString(), $"Timed out after {timeout}.");
        }

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
