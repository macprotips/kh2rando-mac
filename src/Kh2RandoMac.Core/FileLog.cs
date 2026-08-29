namespace Kh2RandoMac.Core;

/// <summary>
/// Append-only diagnostic log at ~/Library/Logs/kh2rando-mac.log so users can attach it
/// to bug reports. Never throws, logging must not break the operation being logged.
/// </summary>
public static class FileLog
{
    /// <summary>
    /// Where diagnostics go. KH2RANDO_LOG_PATH redirects it, which the tests set: a test
    /// run was otherwise writing its deliberately broken cases into the user's real log,
    /// where they read as faults on their machine.
    /// </summary>
    public static string LogPath =>
        Environment.GetEnvironmentVariable("KH2RANDO_LOG_PATH") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Logs", "kh2rando-mac.log");

    private static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                // Simple size cap: start over past 5 MB rather than growing forever.
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5 * 1024 * 1024)
                    File.Delete(LogPath);
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\n");
            }
        }
        catch
        {
            // Never let logging failures surface.
        }
    }
}
