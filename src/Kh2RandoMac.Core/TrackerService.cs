using System.Diagnostics;

namespace Kh2RandoMac.Core;

/// <summary>
/// Installs and launches the community KH2 item tracker (Dee-Ayy/KH2Tracker) inside
/// the game's bottle. Auto-tracking works by reading the game's memory, which is only
/// possible from inside the same Wine session, so the tracker runs as a Windows app
/// next to the game. It is a WPF app and needs the real .NET Framework 4.8; Wine's
/// built-in substitute registers itself as 4.8 but cannot render WPF.
/// </summary>
public class TrackerService
{
    public const string Owner = "Dee-Ayy";
    public const string Repo = "KH2Tracker";
    public const string ExeName = "KhTracker.exe";

    /// <summary>Microsoft's permanent link to the .NET Framework 4.8 offline installer.</summary>
    public const string DotNetInstallerUrl = "https://go.microsoft.com/fwlink/?linkid=2088631";

    public static string TrackerDir(Workspace workspace) => Path.Combine(workspace.Root, "tracker");
    public static string ExePath(Workspace workspace) => Path.Combine(TrackerDir(workspace), ExeName);

    /// <summary>
    /// Whether the real .NET Framework 4.8 is present in the bottle. Wine's stub ships
    /// a handful of files but never clr.dll, the actual runtime, so that is the marker.
    /// </summary>
    public static bool HasDotNet48(Bottle bottle) =>
        File.Exists(Path.Combine(bottle.DriveC, "windows", "Microsoft.NET",
            "Framework64", "v4.0.30319", "clr.dll"));

    public static bool IsInstalled(Workspace workspace, Bottle bottle) =>
        File.Exists(ExePath(workspace)) && HasDotNet48(bottle);

    /// <summary>
    /// Download the tracker and, if needed, install .NET Framework 4.8 into the bottle.
    /// The .NET step is the slow one (15 to 30 minutes) and needs the bottle quit.
    /// </summary>
    public async Task EnsureInstalled(Workspace workspace, Bottle bottle, Action<string> log)
    {
        if (bottle.Platform != WinePlatform.CrossOver)
            throw new InvalidOperationException("The tracker install currently supports CrossOver bottles only.");

        if (!File.Exists(ExePath(workspace)))
        {
            log($"Downloading the latest tracker from {Owner}/{Repo}...");
            var release = await GitHubApi.GetLatestRelease(Owner, Repo);
            var asset = release.Assets.FirstOrDefault(a =>
                    a.Name.Equals(ExeName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"The latest {Repo} release ({release.Tag}) has no {ExeName} download.");
            await GitHubApi.DownloadFile(asset.DownloadUrl, ExePath(workspace));
            log($"Tracker {release.Tag} downloaded.");
        }

        if (HasDotNet48(bottle))
            return;

        if (bottle.IsRunning())
            throw new InvalidOperationException(
                $"Bottle '{bottle.Name}' appears to be running. Quit the game and Steam in CrossOver " +
                "first, then try again; the .NET Framework installer needs the bottle to itself.");

        log("Installing .NET Framework 4.8 into the bottle. One time only, takes 15 to 30 minutes.");

        // Wine's built-in mono registers itself as .NET 4.8, which makes the real
        // installer exit early claiming success. Remove it first.
        var listing = RunWine(bottle, log, quiet: true, "uninstaller", "--list");
        foreach (var (id, name) in ParseUninstallerList(listing))
        {
            if (!name.Contains("Wine Mono", StringComparison.OrdinalIgnoreCase))
                continue;
            log($"Removing '{name}' (Wine's .NET substitute, it blocks the real installer)...");
            RunWine(bottle, log, quiet: true, "uninstaller", "--remove", id);
        }

        var installer = Path.Combine(TrackerDir(workspace), "ndp48.exe");
        if (!File.Exists(installer))
        {
            log("Downloading the .NET Framework 4.8 installer from Microsoft (about 115 MB)...");
            await GitHubApi.DownloadFile(DotNetInstallerUrl, installer);
        }

        // On Windows 10 the framework ships with the OS, so the installer refuses to
        // run there. Pose as Windows 7 for the install, then switch back.
        log("Running the installer (the log stays quiet while it works, that is normal)...");
        RunWine(bottle, log, quiet: true, "winecfg", "-v", "win7");
        try
        {
            var exit = RunWineExe(bottle, log, installer, "/q", "/norestart");
            // 0 = success, 3010 = success but Windows wants a reboot (meaningless in a bottle).
            if (exit != 0 && exit != 3010)
                throw new InvalidOperationException(
                    $".NET Framework installer failed with code {exit}. " +
                    "Check the setup log in the bottle under users/crossover/AppData/Local/Temp.");
        }
        finally
        {
            RunWine(bottle, log, quiet: true, "winecfg", "-v", "win10");
        }

        if (!HasDotNet48(bottle))
            throw new InvalidOperationException(
                ".NET Framework installer finished but the runtime is missing from the bottle. " +
                "Check the setup log in the bottle under users/crossover/AppData/Local/Temp.");
        log(".NET Framework 4.8 installed.");
    }

    /// <summary>Launch the tracker in the bottle. Returns a user-facing note.</summary>
    public static string Launch(Workspace workspace, Bottle bottle)
    {
        if (bottle.Platform != WinePlatform.CrossOver)
            throw new InvalidOperationException("The tracker currently supports CrossOver bottles only.");
        if (!File.Exists(ExePath(workspace)))
            throw new InvalidOperationException("Tracker not installed yet.");

        // The tracker writes its KhTrackerSettings folder into its working directory;
        // anchor it next to the exe so settings live in the workspace.
        var psi = new ProcessStartInfo(CrossOverApp.CxStart)
        {
            UseShellExecute = false,
            WorkingDirectory = TrackerDir(workspace),
        };
        psi.ArgumentList.Add("--bottle");
        psi.ArgumentList.Add(bottle.Name);
        psi.ArgumentList.Add(ExePath(workspace));
        Process.Start(psi);
        return "Tracker starting...";
    }

    /// <summary>
    /// True once the tracker has a window on screen: launching takes Wine a while, and
    /// the process only checks in with macOS as a GUI app when its window appears.
    /// </summary>
    public static bool IsTrackerVisible()
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/lsappinfo", "list")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output.Contains($"\"{ExeName}\" ASN", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Poll until the tracker window is on screen or the timeout passes.</summary>
    public static async Task<bool> WaitUntilVisible(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsTrackerVisible())
                return true;
            await Task.Delay(1000);
        }
        return IsTrackerVisible();
    }

    /// <summary>
    /// Parse `wine uninstaller --list` output: one "id|||Display Name" per line, mixed
    /// with Wine's own logging, which never contains the ||| separator.
    /// </summary>
    public static List<(string Id, string Name)> ParseUninstallerList(string output)
    {
        var result = new List<(string, string)>();
        foreach (var line in output.Split('\n'))
        {
            var idx = line.IndexOf("|||", StringComparison.Ordinal);
            if (idx <= 0)
                continue;
            result.Add((line[..idx].Trim(), line[(idx + 3)..].Trim()));
        }
        return result;
    }

    /// <summary>Run a wine builtin (uninstaller, winecfg) in the bottle and return its output.</summary>
    private static string RunWine(Bottle bottle, Action<string> log, bool quiet, params string[] args)
    {
        var psi = new ProcessStartInfo(CrossOverApp.Wine)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--bottle");
        psi.ArgumentList.Add(bottle.Name);
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (!quiet)
            log(output.Trim());
        return output;
    }

    /// <summary>Run a Windows exe (by mac path) in the bottle and wait; returns its exit code.</summary>
    private static int RunWineExe(Bottle bottle, Action<string> log, string macExePath, params string[] args)
    {
        var psi = new ProcessStartInfo(CrossOverApp.Wine)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--bottle");
        psi.ArgumentList.Add(bottle.Name);
        psi.ArgumentList.Add(bottle.ToWindowsPath(macExePath));
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }
}
