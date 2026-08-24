namespace Kh2RandoMac.Core;

/// <summary>
/// Sikarugir (Wineskin's successor) support. Unlike CrossOver's central bottles,
/// Sikarugir wraps each program in its own .app containing a standard Wine prefix at
/// Contents/SharedSupport/prefix, so all bottle mechanics work unchanged; only
/// discovery and launching differ.
/// </summary>
public static class SikarugirApp
{
    /// <summary>Folders scanned for wrapper apps.</summary>
    private static IEnumerable<string> WrapperLocations()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "Applications", "Sikarugir");
        yield return Path.Combine(home, "Applications");
        yield return "/Applications/Sikarugir";
        yield return "/Applications";
    }

    private static string PrefixIn(string wrapperApp) =>
        Path.Combine(wrapperApp, "Contents", "SharedSupport", "prefix");

    private static bool IsWrapper(string app) =>
        Directory.Exists(Path.Combine(PrefixIn(app), "drive_c"));

    /// <summary>Every Sikarugir wrapper on the system, as a Bottle per wrapper.</summary>
    public static List<Bottle> DiscoverWrappers(IEnumerable<string>? locations = null)
    {
        var result = new List<Bottle>();
        var seen = new HashSet<string>();
        foreach (var location in locations ?? WrapperLocations())
        {
            if (!Directory.Exists(location))
                continue;
            foreach (var app in Directory.GetDirectories(location, "*.app"))
            {
                if (!IsWrapper(app) || !seen.Add(Path.GetFullPath(app)))
                    continue;
                result.Add(new Bottle
                {
                    Name = Path.GetFileNameWithoutExtension(app),
                    Root = PrefixIn(app),
                    Platform = WinePlatform.Sikarugir,
                    WrapperApp = app,
                });
            }
        }
        return result;
    }
}
