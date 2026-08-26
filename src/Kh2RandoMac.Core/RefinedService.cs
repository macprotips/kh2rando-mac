using System.Diagnostics;

namespace Kh2RandoMac.Core;

/// <summary>
/// Support for Kingdom Hearts II Re:Fined (KH-ReFined/KH2-MAIN), the quality-of-life
/// overhaul for the PC port. The mod itself installs like any GitHub mod; what it
/// additionally needs is the .NET 8 Desktop Runtime inside the bottle, because its
/// features live in .NET DLL modules that Panacea loads into the game process.
/// Upstream archived the project in August 2026 with a final build, so the target
/// is stable.
/// </summary>
public class RefinedService
{
    public const string MainMod = "KH-ReFined/KH2-MAIN";

    /// <summary>Microsoft's permanent link to the latest .NET 8 Desktop Runtime (x64) installer.</summary>
    public const string DesktopRuntimeUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe";

    /// <summary>Whether any Re:Fined mod is installed and enabled in the workspace.</summary>
    public static bool AnyRefinedEnabled(Workspace workspace) =>
        workspace.EnabledMods().Any(IsRefinedMod);

    public static bool IsRefinedMod(string modName) =>
        modName.StartsWith("KH-ReFined/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Re:Fined and the randomizer rewrite the same game systems and are not meant to
    /// run together; the community maintains entirely separate setups for each.
    /// Returns the enabled non-Re:Fined gameplay mods so callers can warn.
    /// </summary>
    public static List<string> ConflictingEnabledMods(Workspace workspace)
    {
        if (!AnyRefinedEnabled(workspace))
            return new List<string>();
        return workspace.EnabledMods().Where(m => !IsRefinedMod(m)).ToList();
    }

    /// <summary>
    /// Whether the .NET 8 Desktop Runtime is installed in the bottle. The runtime
    /// installs shared frameworks under Program Files\dotnet; WindowsDesktop.App is
    /// the piece Re:Fined's modules need.
    /// </summary>
    public static bool HasDesktopRuntime(Bottle bottle)
    {
        var shared = Path.Combine(bottle.DriveC, "Program Files", "dotnet",
            "shared", "Microsoft.WindowsDesktop.App");
        return Directory.Exists(shared) &&
            Directory.EnumerateDirectories(shared, "8.*").Any();
    }

    /// <summary>
    /// Download and silently install the .NET 8 Desktop Runtime into the bottle.
    /// A few minutes; needs the bottle quit, same as every bottle-level install.
    /// </summary>
    public async Task EnsureDesktopRuntime(Workspace workspace, Bottle bottle, Action<string> log)
    {
        if (bottle.Platform != WinePlatform.CrossOver)
            throw new InvalidOperationException("The Re:Fined runtime install currently supports CrossOver bottles only.");
        if (HasDesktopRuntime(bottle))
            return;
        if (bottle.IsRunning())
            throw new InvalidOperationException(
                $"Bottle '{bottle.Name}' appears to be running. Quit the game and Steam in CrossOver " +
                "first, then try again; the runtime installer needs the bottle to itself.");

        var installer = Path.Combine(workspace.Root, "runtimes", "windowsdesktop-runtime-8-x64.exe");
        if (!File.Exists(installer))
        {
            log("Downloading the .NET 8 Desktop Runtime from Microsoft (about 60 MB)...");
            await GitHubApi.DownloadFile(DesktopRuntimeUrl, installer);
        }

        log("Installing the .NET 8 Desktop Runtime into the bottle (a few minutes)...");
        var exit = RunInBottle(bottle, installer, "/install", "/quiet", "/norestart");
        FileLog.Write($"[refined] desktop runtime installer exit code: {exit}");
        // 0 = success, 3010 = success but Windows wants a reboot (meaningless in a bottle).
        if (exit != 0 && exit != 3010)
            throw new InvalidOperationException(
                $".NET Desktop Runtime installer failed with code {exit}. Safe to retry.");
        if (!HasDesktopRuntime(bottle))
            throw new InvalidOperationException(
                ".NET Desktop Runtime installer finished but the runtime is missing from the bottle. Safe to retry.");
        log(".NET 8 Desktop Runtime installed.");
    }

    private static int RunInBottle(Bottle bottle, string macExePath, params string[] args)
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
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        var tail = stderr.Length > 400 ? stderr[^400..] : stderr;
        FileLog.Write($"[refined] installer stderr: {tail.Trim()}");
        return p.ExitCode;
    }
}
