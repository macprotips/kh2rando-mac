namespace Kh2RandoMac.Core;

/// <summary>
/// The on-disk layout mirrors OpenKH Mods Manager so mods, seeds and docs translate 1:1:
///   mods/kh2/&lt;author&gt;/&lt;repo&gt;   installed mods
///   mod/kh2/                     compiled (built) mod data Panacea reads
///   data/kh2/                    extracted game data
///   mods-KH2.txt                 enabled mods, one per line, load order top→bottom
/// </summary>
public class Workspace
{
    public string Root { get; }
    public const string Game = "kh2";

    public Workspace(string root)
    {
        Root = root;
    }

    public string ModsDir => Path.Combine(Root, "mods", Game);
    public string CompiledModDir => Path.Combine(Root, "mod", Game);
    /// <summary>Panacea's mod_path, the parent holding one folder per game.</summary>
    public string CompiledModRoot => Path.Combine(Root, "mod");
    public string DataDir => Path.Combine(Root, "data");
    public string GameDataDir => Path.Combine(DataDir, Game);
    public string EnabledModsFile => Path.Combine(Root, "mods-KH2.txt");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ModsDir);
        Directory.CreateDirectory(CompiledModDir);
        Directory.CreateDirectory(DataDir);
    }

    public List<string> EnabledMods() =>
        File.Exists(EnabledModsFile)
            ? File.ReadAllLines(EnabledModsFile).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList()
            : new List<string>();

    public void SaveEnabledMods(IEnumerable<string> mods) =>
        File.WriteAllLines(EnabledModsFile, mods);

    /// <summary>
    /// Display order for every mod, enabled or not. mods-KH2.txt is OpenKH's own file
    /// and lists only the enabled ones, so a mod that is switched off has nowhere to
    /// record its place. This sits beside it rather than changing a format the Windows
    /// tool also reads; absent, the old enabled-first ordering is used.
    /// </summary>
    public string ModOrderFile => Path.Combine(Root, "mod-order.txt");

    public List<string> ModOrder() =>
        File.Exists(ModOrderFile)
            ? File.ReadAllLines(ModOrderFile).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList()
            : new List<string>();

    public void SaveModOrder(IEnumerable<string> mods) =>
        File.WriteAllLines(ModOrderFile, mods);

    /// <summary>All installed mod names ("author/repo" or plain names for zip installs).</summary>
    public List<string> InstalledMods() => ScanMods(ModsDir);

    /// <summary>
    /// Mod names found under any mods directory, laid out as &lt;name&gt; or
    /// &lt;author&gt;/&lt;repo&gt;. Shared so an exported folder can be read the same way
    /// as the live one.
    /// </summary>
    public static List<string> ScanMods(string modsDir)
    {
        var result = new List<string>();
        if (!Directory.Exists(modsDir))
            return result;
        foreach (var top in Directory.GetDirectories(modsDir))
        {
            if (File.Exists(Path.Combine(top, "mod.yml")))
            {
                result.Add(Path.GetFileName(top));
                continue;
            }
            foreach (var nested in Directory.GetDirectories(top))
            {
                if (File.Exists(Path.Combine(nested, "mod.yml")))
                    result.Add($"{Path.GetFileName(top)}/{Path.GetFileName(nested)}");
            }
        }
        return result;
    }

    /// <summary>
    /// Resolve a mod's directory, refusing names that would escape the mods folder
    /// (mod names come from user input and zip filenames).
    /// </summary>
    public string ModPath(string modName)
    {
        if (string.IsNullOrWhiteSpace(modName))
            throw new ArgumentException("Mod name is empty.");
        // TrimEnd: "./" resolves to "<modsdir>/" which would dodge an equality check.
        var full = Path.GetFullPath(Path.Combine(ModsDir, modName)).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(ModsDir).TrimEnd(Path.DirectorySeparatorChar);
        // Must resolve strictly INSIDE the mods dir: a name like "." would resolve to the
        // mods dir itself, and a later Directory.Delete would wipe every installed mod.
        if (full == root || !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException($"Invalid mod name '{modName}'.");
        return full;
    }
}
