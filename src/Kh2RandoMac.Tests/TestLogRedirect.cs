using System.Runtime.CompilerServices;

namespace Kh2RandoMac.Tests;

/// <summary>
/// Sends the diagnostic log somewhere disposable for the duration of a test run.
/// Several tests exercise failure paths on purpose, and those were being appended to
/// the log a real user attaches to bug reports, where they looked like real faults.
/// </summary>
internal static class TestLogRedirect
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        var path = Path.Combine(Path.GetTempPath(), "kh2rando-tests",
            $"testrun-{Guid.NewGuid():N}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Environment.SetEnvironmentVariable("KH2RANDO_LOG_PATH", path);
    }
}
