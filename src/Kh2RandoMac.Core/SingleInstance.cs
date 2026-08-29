namespace Kh2RandoMac.Core;

/// <summary>
/// Keeps one copy of the app in charge of the workspace at a time.
///
/// Two copies running together share one settings file, one mod folder and one bottle,
/// and they do not coordinate: this is how a config gets written by both and ends up
/// unreadable, and how two builds can write the same compiled mod folder. It happens
/// most easily after an update, when an older copy is still sitting on the Desktop.
///
/// A pid file rather than a system lock, because it has to survive the app being killed:
/// a stale file whose process is gone must not lock everyone out for good.
/// </summary>
public static class SingleInstance
{
    private static string LockPath => Path.Combine(AppPaths.ConfigDir, "running.pid");

    /// <summary>The pid of another live copy, or null when this one may proceed.</summary>
    public static int? OtherInstancePid()
    {
        try
        {
            if (!File.Exists(LockPath))
                return null;
            if (!int.TryParse(File.ReadAllText(LockPath).Trim(), out var pid))
                return null;
            if (pid == Environment.ProcessId)
                return null;
            return IsOurAppRunning(pid) ? pid : null;
        }
        catch
        {
            // Unreadable lock: let the app start rather than block it on a side concern.
            return null;
        }
    }

    /// <summary>Claim the workspace for this process.</summary>
    public static void Claim()
    {
        try
        {
            AtomicFile.WriteAllText(LockPath, Environment.ProcessId.ToString());
        }
        catch
        {
            // Not being able to record it is not a reason to refuse to run.
        }
    }

    public static void Release()
    {
        try
        {
            // Only clear it if it is still ours; another copy may have claimed it since.
            if (File.Exists(LockPath) && File.ReadAllText(LockPath).Trim() == Environment.ProcessId.ToString())
                File.Delete(LockPath);
        }
        catch
        {
            // A leftover file is harmless: the pid check retires it.
        }
    }

    /// <summary>
    /// Whether that pid is a copy of this app rather than whatever else the system has
    /// since given the number to.
    /// </summary>
    private static bool IsOurAppRunning(int pid)
    {
        var command = ShellCommand.Run("/bin/ps", "-p", pid.ToString(), "-o", "command=").Output;
        return command.Contains("KH2 Rando Manager", StringComparison.Ordinal);
    }
}
