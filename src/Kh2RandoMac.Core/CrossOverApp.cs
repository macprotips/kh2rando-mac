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

    private static string Bin(string tool)
    {
        var app = AppPath ?? throw new InvalidOperationException(
            "CrossOver not found in /Applications. Install CrossOver first, the game has to run through it.");
        var path = Path.Combine(app, "Contents", "SharedSupport", "CrossOver", "bin", tool);
        if (!File.Exists(path))
            throw new InvalidOperationException($"CrossOver looks incomplete: missing {path}");
        return path;
    }

    public static string CxStart => Bin("cxstart");

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

    private static string? ReadDefault(string domain, string key)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/defaults", $"read {domain} {key}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
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
