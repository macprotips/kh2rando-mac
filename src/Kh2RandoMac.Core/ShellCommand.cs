using System.Diagnostics;

namespace Kh2RandoMac.Core;

/// <summary>What a command-line tool printed and how it ended.</summary>
public record ShellResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Running a short-lived command-line tool and collecting what it printed.
///
/// Both pipes are drained while the process runs rather than one after the other:
/// reading one to completion first blocks forever the moment the other fills, and each
/// of these sits on a path the user is waiting on, so the symptom would be a frozen
/// window. Every call is bounded by a timeout for the same reason.
///
/// Failures come back as an empty result rather than an exception. Every caller is
/// asking a question about the machine ("is this bottle running", "what version is
/// this") and has a sensible answer for not knowing.
/// </summary>
public static class ShellCommand
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static ShellResult Run(string executable, params string[] args) =>
        Run(executable, DefaultTimeout, args);

    public static ShellResult Run(string executable, TimeSpan timeout, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var process = Process.Start(psi);
            if (process == null)
                return new ShellResult(-1, "", "");

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                // A tool that will not answer is worse than no answer: kill it rather
                // than leaving it attached to pipes nobody is reading any more.
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return new ShellResult(-1, "", "");
            }
            return new ShellResult(process.ExitCode,
                output.GetAwaiter().GetResult(),
                error.GetAwaiter().GetResult());
        }
        catch
        {
            return new ShellResult(-1, "", "");
        }
    }
}
