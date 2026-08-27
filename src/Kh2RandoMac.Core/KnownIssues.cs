namespace Kh2RandoMac.Core;

/// <summary>
/// Mods confirmed here to misbehave on current game builds. Entries are things that
/// were reproduced and traced, not rumours, and each note says what actually goes
/// wrong so the player can judge it. These warn and never block: someone may have a
/// reason to run one anyway, and a fixed version can appear at any time.
/// </summary>
public static class KnownIssues
{
    private static readonly Dictionary<string, string> Notes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["thenja09/mastertreasuremagnet"] =
            "Master Treasure Magnet freezes the game. It was built for a version of KH2 from " +
            "before the Steam release and writes to the wrong place in memory every frame, with " +
            "no check to stop it. The mod has not been updated since 2023. Leave it off unless a " +
            "fixed version appears.",
        ["SapphireSapphic/FormMovementPlusPlus"] =
            "Form Movement++ can freeze the game. It was built for a version of KH2 from before " +
            "the Steam release and has no version check, so it may write to the wrong place in " +
            "memory. The mod has not been updated since 2022.",
    };

    /// <summary>The warning for a mod, or null when nothing is known against it.</summary>
    public static string? For(string? modName) =>
        modName != null && Notes.TryGetValue(modName.Trim(), out var note) ? note : null;

    /// <summary>Warnings for every currently enabled mod, in load order.</summary>
    public static List<string> ForEnabled(Workspace workspace) =>
        workspace.EnabledMods()
            .Select(For)
            .Where(note => note != null)
            .Select(note => note!)
            .ToList();
}
