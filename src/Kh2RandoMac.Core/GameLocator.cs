using System.Text.RegularExpressions;

namespace Kh2RandoMac.Core;

public record GameInstall(Bottle Bottle, string GameDirMac, string Launcher);

public static class GameLocator
{
    public const string GameFolderName = "KINGDOM HEARTS -HD 1.5+2.5 ReMIX-";
    public const string Kh2ExeName = "KINGDOM HEARTS II FINAL MIX.exe";

    /// <summary>Search every CrossOver bottle for a KH 1.5+2.5 install (Steam libraries and Epic locations).</summary>
    public static List<GameInstall> FindAll()
    {
        var results = new List<GameInstall>();
        foreach (var bottle in Bottle.Discover())
        {
            foreach (var lib in SteamLibraries(bottle))
            {
                var candidate = Path.Combine(lib, "steamapps", "common", GameFolderName);
                if (IsGameDir(candidate))
                    results.Add(new GameInstall(bottle, candidate, "Steam"));
            }
            // Epic default layout: <drive>/KH_1.5_2.5 or Program Files/Epic Games/KH_1.5_2.5
            foreach (var (_, driveRoot) in bottle.DriveMappings())
            {
                foreach (var rel in new[] { "KH_1.5_2.5", "Program Files/Epic Games/KH_1.5_2.5" })
                {
                    var candidate = Path.Combine(driveRoot, rel);
                    if (IsGameDir(candidate))
                        results.Add(new GameInstall(bottle, candidate, "EGS"));
                }
            }
        }
        return results.DistinctBy(g => g.GameDirMac).ToList();
    }

    public static bool IsGameDir(string dir) =>
        Directory.Exists(dir) && File.Exists(Path.Combine(dir, Kh2ExeName));

    /// <summary>All Steam library roots reachable from a bottle, as mac paths.</summary>
    private static IEnumerable<string> SteamLibraries(Bottle bottle)
    {
        var steamRoot = Path.Combine(bottle.DriveC, "Program Files (x86)", "Steam");
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            yield break;
        yield return steamRoot;
        foreach (var winPath in ParseLibraryFolders(File.ReadAllText(vdf)))
        {
            var mac = bottle.ToMacPath(winPath);
            if (mac != null && Directory.Exists(mac))
                yield return mac;
        }
    }

    /// <summary>Windows paths of every Steam library listed in a libraryfolders.vdf.</summary>
    public static List<string> ParseLibraryFolders(string vdfText) =>
        Regex.Matches(vdfText, "\"path\"\\s+\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value.Replace("\\\\", "\\"))
            .ToList();

    /// <summary>Detect the Image/&lt;lang&gt; folder that holds kh2_first.hed. Returns null if not found.</summary>
    public static string? DetectLanguageFolder(string gameDir)
    {
        var image = Path.Combine(gameDir, "Image");
        if (!Directory.Exists(image))
            return null;
        foreach (var sub in Directory.GetDirectories(image))
        {
            if (File.Exists(Path.Combine(sub, "kh2_first.hed")))
                return Path.GetFileName(sub);
        }
        return null;
    }
}
