namespace Kh2RandoMac.Core;

/// <summary>What a dropped folder turned out to be.</summary>
public enum FolderKind
{
    /// <summary>Nothing recognisable: no mods inside, no mod.yml.</summary>
    Unknown,
    /// <summary>A folder written by Export: a mods directory, usually with a load order.</summary>
    Export,
    /// <summary>A single mod, i.e. a folder with a mod.yml in it.</summary>
    SingleMod,
}

/// <summary>
/// The other half of <see cref="ExportService"/>: takes an exported folder (or a lone
/// mod folder) and puts it into the workspace. Restoring someone else's setup by hand
/// is slow and gets the load order wrong, which is the part that decides conflicts.
/// </summary>
public static class ImportService
{
    /// <summary>
    /// Refuse a source that lives inside the workspace's own mods folder. Import
    /// deletes the destination before copying, so importing a folder from in there
    /// would delete the very files it is about to copy and report success.
    /// </summary>
    private static void RefuseSourceInsideWorkspace(Workspace workspace, string source)
    {
        var mods = Path.GetFullPath(workspace.ModsDir).TrimEnd(Path.DirectorySeparatorChar);
        var from = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        if (from == mods || from.StartsWith(mods + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "That folder is already inside the app's own mods folder, so there is nothing to import.");
    }

    public static FolderKind Identify(string folder)
    {
        if (!Directory.Exists(folder))
            return FolderKind.Unknown;
        if (File.Exists(Path.Combine(folder, "mod.yml")))
            return FolderKind.SingleMod;
        if (Workspace.ScanMods(Path.Combine(folder, ExportService.ModsFolderName)).Count > 0)
            return FolderKind.Export;
        // Tolerate someone handing over just the inner mods folder.
        return Workspace.ScanMods(folder).Count > 0 ? FolderKind.Export : FolderKind.Unknown;
    }

    /// <summary>The mods directory inside an export, whichever of the two shapes it is.</summary>
    public static string ModsDirOf(string folder)
    {
        var nested = Path.Combine(folder, ExportService.ModsFolderName);
        return Workspace.ScanMods(nested).Count > 0 ? nested : folder;
    }

    /// <summary>Mod names an export would bring in, for confirming before anything is written.</summary>
    public static List<string> Preview(string folder) => Workspace.ScanMods(ModsDirOf(folder));

    /// <summary>
    /// Copy every mod from the export into the workspace, replacing same-named mods.
    /// When the export carries a load order and <paramref name="applyLoadOrder"/> is
    /// set, it replaces the current one; the previous order is kept alongside it as
    /// a .bak so a mistaken import is recoverable.
    /// </summary>
    public static int Import(Workspace workspace, string folder, bool applyLoadOrder, Action<string>? log = null)
    {
        RefuseSourceInsideWorkspace(workspace, folder);
        var modsDir = ModsDirOf(folder);
        var mods = Workspace.ScanMods(modsDir);
        if (mods.Count == 0)
            throw new InvalidOperationException("That folder has no mods in it.");

        workspace.EnsureDirectories();
        foreach (var mod in mods)
        {
            log?.Invoke($"Importing {mod}...");
            DirectoryOps.Replace(
                Path.Combine(modsDir, mod.Replace('/', Path.DirectorySeparatorChar)),
                workspace.ModPath(mod));
        }

        var order = Path.Combine(folder, ExportService.OrderFileName);
        if (applyLoadOrder && File.Exists(order))
        {
            if (File.Exists(workspace.EnabledModsFile))
                File.Copy(workspace.EnabledModsFile, workspace.EnabledModsFile + ".bak", true);
            File.Copy(order, workspace.EnabledModsFile, true);
            var display = Path.Combine(folder, ExportService.DisplayOrderFileName);
            if (File.Exists(display))
                File.Copy(display, workspace.ModOrderFile, true);
            else
                File.Delete(workspace.ModOrderFile);
            log?.Invoke("Load order replaced (the previous one is kept as mods-KH2.txt.bak).");
        }
        return mods.Count;
    }

    /// <summary>Install one mod folder, replacing any mod already using that name.</summary>
    public static string ImportSingleMod(Workspace workspace, string folder, Action<string>? log = null)
    {
        if (!File.Exists(Path.Combine(folder, "mod.yml")))
            throw new InvalidOperationException("That folder has no mod.yml, so it is not a mod.");
        RefuseSourceInsideWorkspace(workspace, folder);
        workspace.EnsureDirectories();
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        DirectoryOps.Replace(folder, workspace.ModPath(name));
        log?.Invoke($"Imported {name}.");
        return name;
    }

}
