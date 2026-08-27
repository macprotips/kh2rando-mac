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

    /// <summary>
    /// The CrossOver that owns a bottle. Stable and Preview can both be installed, and
    /// a bottle records the version that last touched it: running a Preview-upgraded
    /// bottle with the older stable build fails with "failed to load start.exe" and a
    /// bottle-update error. Matches on version, falling back to whatever is installed.
    /// </summary>
    public static string? AppPathForVersion(string? bottleVersion)
    {
        var installed = CandidateApps.Where(Directory.Exists).ToList();
        if (bottleVersion == null || installed.Count < 2)
            return installed.FirstOrDefault();
        return installed.FirstOrDefault(a => BundleVersion(a) == bottleVersion)
            ?? installed.FirstOrDefault();
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
