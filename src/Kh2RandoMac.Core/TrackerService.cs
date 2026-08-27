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
    /// Whether the real .NET Framework 4.8 is present in the bottle. Wine's mono
    /// substitute mimics much of .NET, and newer versions even ship a clr.dll, which
    /// burned us as a marker once. Require what only the real install has: WPF's
    /// native renderer (mono has no WPF, and the tracker needs it to draw anything)
    /// and a full-size clr.dll (the real one is about 11 MB; shims are small).
    /// </summary>
    public static bool HasDotNet48(Bottle bottle)
    {
        var framework = Path.Combine(bottle.DriveC, "windows", "Microsoft.NET",
            "Framework64", "v4.0.30319");
        var clr = new FileInfo(Path.Combine(framework, "clr.dll"));
        return File.Exists(Path.Combine(framework, "WPF", "wpfgfx_v0400.dll"))
            && clr.Exists && clr.Length > 5_000_000;
    }

    /// <summary>One log line of the raw facts HasDotNet48 decides on, for field logs.</summary>
    public static void LogDotNetState(Bottle bottle)
    {
        var framework = Path.Combine(bottle.DriveC, "windows", "Microsoft.NET",
            "Framework64", "v4.0.30319");
        var clr = new FileInfo(Path.Combine(framework, "clr.dll"));
        var wpf = File.Exists(Path.Combine(framework, "WPF", "wpfgfx_v0400.dll"));
        bottle.GetDllOverrides().TryGetValue("mscoree", out var mscoree);
        // The loader dll sizes tell a real install (Microsoft's files) apart from
        // Wine's stubs when the big marker files alone look complete.
        var ms64 = new FileInfo(Path.Combine(bottle.DriveC, "windows", "system32", "mscoree.dll"));
        var ms32 = new FileInfo(Path.Combine(bottle.DriveC, "windows", "syswow64", "mscoree.dll"));
        FileLog.Write($"[tracker] detection: wpfgfx={wpf} clr={(clr.Exists ? clr.Length : 0)} " +
            $"verdict={HasDotNet48(bottle)} mscoree={mscoree ?? "unset"} " +
            $"loader64={(ms64.Exists ? ms64.Length : 0)} loader32={(ms32.Exists ? ms32.Length : 0)} " +
            $"bottle={bottle.Name}");
    }

    /// <summary>
    /// Whether the bottle still needs pinning to the real framework. Wine prefers its
    /// mono substitute over an installed real .NET unless mscoree is overridden, and
    /// the substitute cannot render the tracker. Field-proven: a bottle can have the
    /// real framework installed and still crash exactly like a bottle without it.
    /// </summary>
    public static bool NeedsRuntimePin(Bottle bottle) =>
        !bottle.GetDllOverrides().TryGetValue("mscoree", out var mode) ||
        !mode.Contains("native");

    /// <summary>Pin the bottle to the real framework. Refuses while the bottle runs.</summary>
    public static void PinRuntime(Bottle bottle) =>
        bottle.EnsureDllOverrides(new[] { "mscoree" });

    public static bool IsInstalled(Workspace workspace, Bottle bottle) =>
        File.Exists(ExePath(workspace)) && HasDotNet48(bottle);

    /// <summary>
    /// Download the tracker and, if needed, install .NET Framework 4.8 into the bottle.
    /// The .NET step is the slow one (a few minutes) and needs the bottle quit.
    /// With force, the removal and install run even when detection says the framework
    /// is present: the repair path for a bottle where a broken earlier install left
    /// enough files to pass detection but not enough to run.
    /// </summary>
    public async Task EnsureInstalled(Workspace workspace, Bottle bottle, Action<string> log, bool force = false)
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

        if (!force && HasDotNet48(bottle))
            return;

        if (bottle.IsRunning())
            throw new InvalidOperationException(
                $"Bottle '{bottle.Name}' appears to be running. Quit the game and Steam in CrossOver " +
                "first, then try again; the .NET Framework installer needs the bottle to itself.");

        log("Installing .NET Framework 4.8 into the bottle. One time only, takes a few minutes.");

        // Wine's built-in mono registers itself as .NET 4.8, which makes the real
        // installer exit early claiming success. Remove it first.
        var listing = RunWine(bottle, log, quiet: true, "uninstaller", "--list");
        var entries = ParseUninstallerList(listing);
        FileLog.Write($"[tracker] bottle packages: {string.Join("; ", entries.Select(e => e.Name))}");
        foreach (var (id, name) in entries)
        {
            if (!IsMonoPackage(name))
                continue;
            log($"Removing '{name}' (Wine's .NET substitute, it blocks the real installer)...");
            RunWine(bottle, log, quiet: true, "uninstaller", "--remove", id);
        }

        // The mono uninstall does not always clear the registry markers that claim
        // .NET 4.x is installed, and the installer trusts them. Delete them outright;
        // the real installer recreates them. reg delete errors on a missing key,
        // which is fine.
        RunWine(bottle, log, quiet: true, "reg", "delete",
            @"HKLM\Software\Microsoft\NET Framework Setup\NDP\v4", "/f");
        RunWine(bottle, log, quiet: true, "reg", "delete",
            @"HKLM\Software\Wow6432Node\Microsoft\NET Framework Setup\NDP\v4", "/f");

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
            FileLog.Write($"[tracker] ndp48 installer exit code: {exit}");
            // 0 = success, 3010 = success but Windows wants a reboot (meaningless in a bottle).
            if (exit != 0 && exit != 3010)
                throw new InvalidOperationException(
                    $".NET Framework installer failed with code {exit}. " +
                    $"Installer verdict: {ReadSetupLogSummary(bottle) ?? "no setup log found"}. " +
                    "Try clicking Tracker again; the install is safe to retry.");
        }
        finally
        {
            RunWine(bottle, log, quiet: true, "winecfg", "-v", "win10");
        }

        if (!HasDotNet48(bottle))
            throw new InvalidOperationException(
                ".NET Framework installer finished but the runtime is missing from the bottle. " +
                $"Installer verdict: {ReadSetupLogSummary(bottle) ?? "no setup log found"}. " +
                "Try clicking Tracker again; the install is safe to retry.");
        log(".NET Framework 4.8 installed.");

        // Wine's builtin mscoree prefers its mono substitute when present; a later
        // CrossOver update can quietly reinstall mono into the bottle, which would
        // put the tracker back on the runtime that cannot render it. Pin the bottle
        // to the real framework. The installer's wineserver can linger briefly, and
        // the registry edit refuses while it runs; give it a moment.
        for (var i = 0; i < 30 && bottle.IsRunning(); i++)
            await Task.Delay(1000);
        PinRuntime(bottle);
        log("Bottle pinned to the real .NET Framework.");
    }

    /// <summary>Names Wine's .NET substitute goes by across Wine and CrossOver versions.</summary>
    public static bool IsMonoPackage(string name) =>
        name.Contains("Mono", StringComparison.OrdinalIgnoreCase) &&
        (name.Contains("Wine", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("CrossOver", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The one line worth surfacing from Microsoft's setup log: its own verdict.
    /// The installer writes an HTML log per run; read the newest.
    /// </summary>
    public static string? ReadSetupLogSummary(Bottle bottle)
    {
        try
        {
            var usersDir = Path.Combine(bottle.DriveC, "users");
            if (!Directory.Exists(usersDir))
                return null;
            var newest = Directory.GetDirectories(usersDir)
                .Select(u => Path.Combine(u, "AppData", "Local", "Temp"))
                .Where(Directory.Exists)
                .SelectMany(t => Directory.GetFiles(t, "Microsoft .NET Framework 4.8 Setup_*.html"))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (newest == null)
                return null;
            var match = System.Text.RegularExpressions.Regex.Match(
                File.ReadAllText(newest), @"Final Result: [^<]*");
            return match.Success ? match.Value.Trim() : $"no verdict line in {Path.GetFileName(newest)}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Launch the tracker in the bottle. Returns the wine process so callers can tell
    /// "still starting" from "crashed"; its output is recorded to the app log when it
    /// exits, because a WPF startup crash is otherwise invisible.
    /// </summary>
    public static Process Launch(Workspace workspace, Bottle bottle)
    {
        if (bottle.Platform != WinePlatform.CrossOver)
            throw new InvalidOperationException("The tracker currently supports CrossOver bottles only.");
        if (!File.Exists(ExePath(workspace)))
            throw new InvalidOperationException("Tracker not installed yet.");

        // The tracker writes its KhTrackerSettings folder into its working directory;
        // anchor it next to the exe so settings live in the workspace.
        var psi = new ProcessStartInfo(CrossOverApp.Wine)
        {
            UseShellExecute = false,
            WorkingDirectory = TrackerDir(workspace),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--bottle");
        psi.ArgumentList.Add(bottle.Name);
        psi.ArgumentList.Add(ExePath(workspace));
        var p = Process.Start(psi)!;
        _ = Task.Run(async () =>
        {
            try
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                static string Tail(string s) => s.Length > 1500 ? s[^1500..] : s;
                FileLog.Write($"[tracker] wine process exited code={p.ExitCode}");
                FileLog.Write($"[tracker] stdout tail: {Tail(await stdout).Trim()}");
                FileLog.Write($"[tracker] stderr tail: {Tail(await stderr).Trim()}");
            }
            catch (Exception ex)
            {
                FileLog.Write($"[tracker] output capture failed: {ex.Message}");
            }
        });
        return p;
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

    private static string Tail(string s, int max = 400) => s.Length > max ? s[^max..] : s;

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
        // Both pipes must be drained concurrently: Wine writes a steady stream of
        // fixme/err chatter to stderr, and reading stdout to completion first
        // deadlocks once that pipe fills, which hangs the whole install.
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        var output = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        var tail = stderr.Length > 400 ? stderr[^400..] : stderr;
        FileLog.Write($"[tracker] wine {args.FirstOrDefault()} exit={p.ExitCode} stderr: {tail.Trim()}");
        if (!quiet)
            log(output.Trim());
        // Some CrossOver versions print command output on stderr (seen with
        // uninstaller --list); parsers get both streams.
        return output + "\n" + stderr;
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
        // Drain both pipes concurrently; the .NET Framework installer runs for
        // minutes and Wine fills stderr while it does.
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        FileLog.Write($"[tracker] installer stderr tail: " +
            Tail(stderrTask.GetAwaiter().GetResult()));
        stdoutTask.GetAwaiter().GetResult();
        return p.ExitCode;
    }
}
