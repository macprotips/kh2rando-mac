using System.Diagnostics;

namespace Kh2RandoMac.Core;

/// <summary>Locates the CrossOver installation (regular or Preview) and its CLI tools.</summary>
public static class CrossOverApp
{
    private static readonly string[] CandidateApps =
    {
        "/Applications/CrossOver.app",
        "/Applications/CrossOver Preview.app",
    };

    public static string? AppPath =>
        CandidateApps.FirstOrDefault(Directory.Exists);

    public static bool IsInstalled => AppPath != null;

    /// <summary>A CrossOver app's full version, e.g. "27.0.0.40817".</summary>
    private static string? BundleVersion(string appPath) =>
        ReadDefault(Path.Combine(appPath, "Contents", "Info.plist"), "CFBundleVersion");

    /// <summary>Every CrossOver installed, newest first, with its version.</summary>
    public static List<(string Path, Version Version)> Installed() =>
        CandidateApps.Where(Directory.Exists)
            .Select(a => (Path: a, Version: Version.TryParse(BundleVersion(a) ?? "", out var v) ? v : new Version(0, 0)))
            .OrderByDescending(a => a.Version)
            .ToList();

    /// <summary>A short label for a menu, e.g. "CrossOver Preview (27.0.0)".</summary>
    public static string DescribeApp(string appPath)
    {
        var name = Path.GetFileNameWithoutExtension(appPath);
        var v = BundleVersion(appPath);
        return v == null ? name : $"{name} ({v})";
    }

    /// <summary>
    /// The CrossOver to run a bottle with. A copy can open a bottle its own age or
    /// older, upgrading it on the way, but never one from a newer version: that fails
    /// with "failed to load start.exe" and a bottle-update error. So pick the oldest
    /// copy that is still new enough, which runs the bottle without dragging it up to
    /// a newer version and locking the older copy out of it. Falls back to the newest
    /// installed when the bottle is newer than anything here, which at least reports a
    /// real error instead of a confusing one.
    /// </summary>
    public static string? AppPathForVersion(string? bottleVersion, string? preferred = null)
    {
        var installed = Installed();
        if (installed.Count == 0)
            return null;
        // An explicit choice wins, as long as it is still installed.
        if (preferred != null && installed.Any(a => a.Path == preferred))
            return preferred;
        if (bottleVersion == null || !Version.TryParse(bottleVersion, out var needed))
            return installed[0].Path;
        var capable = installed.Where(a => a.Version >= needed).ToList();
        return capable.Count > 0 ? capable[^1].Path : installed[0].Path;
    }

    private static string Bin(string tool) => Bin(tool, AppPath);

    /// <summary>A CLI tool inside a specific CrossOver app.</summary>
    public static string BinIn(string tool, string? appPath) => Bin(tool, appPath);

    private static string Bin(string tool, string? appPath)
    {
        var app = appPath ?? throw new InvalidOperationException(
            "CrossOver not found in /Applications. Install CrossOver first, the game has to run through it.");
        var path = Path.Combine(app, "Contents", "SharedSupport", "CrossOver", "bin", tool);
        if (!File.Exists(path))
            throw new InvalidOperationException($"CrossOver looks incomplete: missing {path}");
        return path;
    }

    public static string CxStart => Bin("cxstart");

    public static string Wine => Bin("wine");

    private static readonly Lazy<string> _bottlesRoot = new(() =>
    {
        var custom = ReadDefault("com.codeweavers.CrossOver", "BottleDir");
        if (!string.IsNullOrEmpty(custom) && Directory.Exists(custom))
            return custom;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "CrossOver", "Bottles");
    });

    /// <summary>
    /// The bottles directory. CrossOver lets users relocate it (BottleDir preference);
    /// falls back to the default location. Cached, it can't change mid-run and reading
    /// the preference spawns a process.
    /// </summary>
    public static string BottlesRoot => _bottlesRoot.Value;

    /// <summary>Read one key with `defaults read`, from a domain or a plist path.</summary>
    private static string? ReadDefault(string domainOrPlist, string key)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/defaults")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("read");
            psi.ArgumentList.Add(domainOrPlist);
            psi.ArgumentList.Add(key);
            using var p = Process.Start(psi);
            if (p == null)
                return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(3000);
            return p.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
