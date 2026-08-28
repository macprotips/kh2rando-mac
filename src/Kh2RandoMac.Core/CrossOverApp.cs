using System.Diagnostics;

namespace Kh2RandoMac.Core;

/// <summary>Locates the CrossOver installation (regular or Preview) and its CLI tools.</summary>
public static class CrossOverApp
{
    public const string BundleId = "com.codeweavers.CrossOver";

    /// <summary>Folders people keep applications in; copies elsewhere are found by Spotlight.</summary>
    private static readonly string[] SearchDirs =
    {
        "/Applications",
        "/Applications/CrossOver",
    };

    /// <summary>A folder is a CrossOver install only if it can actually run something.</summary>
    private static bool IsCrossOver(string appPath) =>
        File.Exists(Path.Combine(appPath, "Contents", "SharedSupport", "CrossOver", "bin", "wine"));

    private static readonly Lazy<List<string>> _discovered = new(DiscoverApps);

    /// <summary>
    /// Every CrossOver on the machine, wherever it lives. People keep older versions
    /// beside the current one, rename them, and install to their home folder, so a
    /// fixed list of paths misses copies they are entitled to use. Spotlight finds
    /// them by bundle id; the directory scan covers the case where it is unavailable.
    /// Cached: this shells out, and installs do not appear mid-session.
    /// </summary>
    private static List<string> DiscoverApps()
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var dir in SearchDirs.Concat(new[] { Path.Combine(home, "Applications") }))
        {
            try
            {
                if (Directory.Exists(dir))
                    foreach (var app in Directory.EnumerateDirectories(dir, "*.app"))
                        if (IsCrossOver(app))
                            found.Add(app);
            }
            catch
            {
                // Unreadable folder: the other sources still apply.
            }
        }
        foreach (var app in Spotlight($"kMDItemCFBundleIdentifier == '{BundleId}'"))
            if (IsCrossOver(app))
                found.Add(app);
        return found.ToList();
    }

    private static List<string> Spotlight(string query)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/mdfind") { RedirectStandardOutput = true };
            psi.ArgumentList.Add(query);
            using var p = Process.Start(psi);
            if (p == null)
                return new List<string>();
            var lines = p.StandardOutput.ReadToEnd()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .ToList();
            p.WaitForExit(5000);
            return lines;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>A CrossOver app's full version, e.g. "27.0.0.40817".</summary>
    private static string? BundleVersion(string appPath) =>
        ReadDefault(Path.Combine(appPath, "Contents", "Info.plist"), "CFBundleVersion");

    /// <summary>
    /// Every CrossOver installed, newest first, with its version both parsed, for
    /// comparing against a bottle, and as the bundle writes it, for showing someone: a
    /// build string that does not parse into a Version still means something to them.
    /// </summary>
    public static List<(string Path, Version Version, string VersionText)> Installed() =>
        _discovered.Value
            .Select(a =>
            {
                var text = BundleVersion(a);
                var version = Version.TryParse(text ?? "", out var v) ? v : new Version(0, 0);
                return (Path: a, Version: version, VersionText: text ?? version.ToString());
            })
            .OrderByDescending(a => a.Version)
            .ThenBy(a => a.Path, StringComparer.Ordinal)
            .ToList();

    /// <summary>The newest CrossOver installed, used when no bottle is in play.</summary>
    public static string? AppPath => Installed().FirstOrDefault().Path;

    public static bool IsInstalled => Installed().Count > 0;

    /// <summary>
    /// Labels for a menu, disambiguated only where they need to be. Several copies of
    /// the same version, or the same name in different folders, are common once people
    /// keep old releases around. Versions come from the caller, which already read them
    /// off disk, so a label depends on nothing but the arguments.
    /// </summary>
    public static List<string> DescribeAll(IReadOnlyList<(string Path, Version Version, string VersionText)> apps)
    {
        var labels = apps
            .Select(a => $"{Path.GetFileNameWithoutExtension(a.Path)} ({a.VersionText})")
            .ToList();
        // Add the location to any label that appears more than once, then fall back to
        // the full path if two are somehow still identical. A menu of choices that read
        // the same is worse than no menu.
        for (var pass = 0; pass < 2; pass++)
        {
            var duplicated = labels.GroupBy(l => l).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            if (duplicated.Count == 0)
                break;
            for (var i = 0; i < labels.Count; i++)
            {
                if (!duplicated.Contains(labels[i]))
                    continue;
                labels[i] = pass == 0
                    ? $"{labels[i]} in {LocationLabel(apps[i].Path)}"
                    : apps[i].Path;
            }
        }
        return labels;
    }

    /// <summary>Where a copy lives, in words someone would recognise in a menu.</summary>
    private static string LocationLabel(string appPath)
    {
        var dir = Path.GetDirectoryName(appPath) ?? "";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (dir == "/Applications")
            return "Applications";
        if (dir == Path.Combine(home, "Applications"))
            return "your Applications folder";
        if (dir == Path.Combine(home, "Downloads"))
            return "Downloads";
        return Path.GetFileName(dir) is { Length: > 0 } name ? name : dir;
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
        if (capable.Count == 0)
            return installed[0].Path;
        // Oldest capable copy, so the bottle is not dragged to a newer version; among
        // copies of that same version, the one in a real applications folder rather
        // than a leftover in Downloads or on a disk image.
        var oldest = capable.Min(a => a.Version);
        return capable.Where(a => a.Version == oldest)
            .OrderBy(a => LocationRank(a.Path))
            .First().Path;
    }

    /// <summary>Lower is more likely to be the copy someone actually uses.</summary>
    private static int LocationRank(string appPath)
    {
        var dir = Path.GetDirectoryName(appPath) ?? "";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (dir == "/Applications")
            return 0;
        if (dir == Path.Combine(home, "Applications"))
            return 1;
        return dir.StartsWith(Path.Combine(home, "Downloads"), StringComparison.Ordinal) ? 3 : 2;
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

    // No global CxStart/Wine: which copy to use depends on the bottle, and reaching
    // for "whichever is installed" is how the tracker ended up launching through the
    // wrong one. Callers pass a bottle's OwningApp to BinIn instead.

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
