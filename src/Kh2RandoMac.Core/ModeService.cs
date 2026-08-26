namespace Kh2RandoMac.Core;

/// <summary>
/// Two ways to play, one game folder: the randomizer and Re:Fined rewrite the same
/// game systems and never run together, so instead of making users juggle checkboxes,
/// the Manager keeps a separate enabled-mod list per mode and switches between them.
/// mods-KH2.txt always holds the ACTIVE list (everything else already reads it);
/// the parked mode's list waits in mods-KH2.&lt;mode&gt;.txt.
/// </summary>
public static class ModeService
{
    public const string Rando = "rando";
    public const string Refined = "refined";

    public static string ParkedListFile(Workspace workspace, string mode) =>
        Path.Combine(workspace.Root, $"mods-KH2.{mode}.txt");

    public static string Normalize(string? mode) =>
        string.Equals(mode, Refined, StringComparison.OrdinalIgnoreCase) ? Refined : Rando;

    /// <summary>
    /// Park the current list under the current mode and activate the other mode's
    /// list. A mode switched to for the first time starts with a sensible default:
    /// Re:Fined mode enables the installed Re:Fined mods, rando mode starts empty.
    /// The caller saves the config. Returns the new mode.
    /// </summary>
    public static string Switch(AppConfig config, Workspace workspace)
    {
        var current = Normalize(config.ActiveMode);
        var next = current == Rando ? Refined : Rando;

        File.WriteAllLines(ParkedListFile(workspace, current), workspace.EnabledMods());

        var parkedFile = ParkedListFile(workspace, next);
        var newList = File.Exists(parkedFile)
            ? File.ReadAllLines(parkedFile).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList()
            : DefaultList(workspace, next);
        workspace.SaveEnabledMods(newList);

        config.ActiveMode = next;
        return next;
    }

    private static List<string> DefaultList(Workspace workspace, string mode) =>
        mode == Refined
            ? workspace.InstalledMods().Where(RefinedService.IsRefinedMod).ToList()
            : new List<string>();
}
