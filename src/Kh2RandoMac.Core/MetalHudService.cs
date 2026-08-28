namespace Kh2RandoMac.Core;

/// <summary>
/// Toggles Apple's Metal Performance HUD (FPS overlay) for one bottle only, by
/// setting MTL_HUD_ENABLED in the bottle's cxbottle.conf [EnvironmentVariables]
/// section. Nothing outside the bottle is affected. CrossOver reads the file when
/// launching bottle programs, so Steam and the game must be restarted to see a
/// change. CrossOver bottles only; Sikarugir wrappers have no cxbottle.conf.
/// </summary>
public static class MetalHudService
{
    private const string Section = "[EnvironmentVariables]";
    private const string Line = "\"MTL_HUD_ENABLED\" = \"1\"";

    private static string? ConfPath(Bottle bottle)
    {
        if (bottle.Platform != WinePlatform.CrossOver)
            return null;
        var path = Path.Combine(bottle.Root, "cxbottle.conf");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Null when the bottle doesn't support the toggle (Sikarugir, missing conf).</summary>
    public static bool? IsEnabled(Bottle bottle)
    {
        var path = ConfPath(bottle);
        if (path == null)
            return null;
        return File.ReadLines(path).Any(l => l.Trim() == Line);
    }

    public static void SetEnabled(Bottle bottle, bool enabled)
    {
        var path = ConfPath(bottle)
            ?? throw new InvalidOperationException("The FPS HUD toggle needs a CrossOver bottle.");
        // CrossOver owns this file while the bottle is up and rewrites it on the way
        // out, so an edit made now would simply disappear. Every other bottle-level
        // change in the app refuses on the same grounds.
        if (bottle.IsRunning())
            throw new InvalidOperationException(
                $"{bottle.WhatIsUsingIt()}, then try again. CrossOver would otherwise overwrite the change.");
        var lines = File.ReadAllLines(path).ToList();
        lines.RemoveAll(l => l.Trim().StartsWith("\"MTL_HUD_ENABLED\"", StringComparison.Ordinal));

        if (enabled)
        {
            var section = lines.FindIndex(l => l.Trim() == Section);
            if (section < 0)
            {
                lines.Add("");
                lines.Add(Section);
                lines.Add(Line);
            }
            else
            {
                var end = section + 1;
                while (end < lines.Count && !lines[end].TrimStart().StartsWith("["))
                    end++;
                while (end > section + 1 && string.IsNullOrWhiteSpace(lines[end - 1]))
                    end--;
                lines.Insert(end, Line);
            }
        }

        File.Copy(path, path + ".kh2rando.bak", true);
        AtomicFile.WriteAllLines(path, lines);
    }
}
