namespace Kh2RandoMac.Core;

/// <summary>
/// Copies the installed mods and the load order into a folder the user can back up,
/// move to another Mac, or hand to someone else. The export is a plain folder of
/// files on purpose: no archive format to get wrong, and anyone can look inside it.
/// Everything goes inside one named folder so exporting to somewhere like the Desktop
/// leaves a single item to zip and send, not loose files.
/// </summary>
public static class ExportService
{
    public const string ModsFolderName = "mods";
    /// <summary>Companion to the load order: where every mod sits, enabled or not.</summary>
    public const string DisplayOrderFileName = "mod-order.txt";

    public const string OrderFileName = "mods-KH2.txt";
    public const string ReadmeName = "README.txt";
    public const string FolderName = "KH2 Rando Export";

    /// <summary>Total bytes the export will copy, so callers can warn before a slow one.</summary>
    public static long EstimateSize(Workspace workspace) =>
        Directory.Exists(workspace.ModsDir)
            ? Directory.EnumerateFiles(workspace.ModsDir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length)
            : 0;

    public static string DescribeSize(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GB"
        : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0} MB"
        : $"{Math.Max(1, bytes / 1024)} KB";

    /// <summary>
    /// Write the export into a new folder inside <paramref name="destination"/> and
    /// return that folder's path. An existing export is never overwritten: the next
    /// free name is used instead.
    /// </summary>
    public static string Export(Workspace workspace, string destination, Action<string>? log = null)
    {
        var mods = workspace.InstalledMods();
        if (mods.Count == 0)
            throw new InvalidOperationException("There are no installed mods to export.");

        destination = NextFreeFolder(destination);
        var modsOut = Path.Combine(destination, ModsFolderName);
        Directory.CreateDirectory(modsOut);
        foreach (var mod in mods)
        {
            log?.Invoke($"Copying {mod}...");
            DirectoryOps.Copy(workspace.ModPath(mod), Path.Combine(modsOut, mod));
        }

        // The load order is the half people forget, and it is the half that decides
        // which mod wins a conflict.
        if (File.Exists(workspace.EnabledModsFile))
            File.Copy(workspace.EnabledModsFile, Path.Combine(destination, OrderFileName), true);
        // The arrangement of the disabled mods travels alongside it, so an imported set
        // looks the way it did rather than sorting them to the bottom.
        if (File.Exists(workspace.ModOrderFile))
            File.Copy(workspace.ModOrderFile, Path.Combine(destination, DisplayOrderFileName), true);

        File.WriteAllText(Path.Combine(destination, ReadmeName), Readme(mods.Count));
        return destination;
    }

    /// <summary>"KH2 Rando Export", or the next free numbered variant beside it.</summary>
    private static string NextFreeFolder(string parent)
    {
        var candidate = Path.Combine(parent, FolderName);
        for (var n = 2; Directory.Exists(candidate); n++)
            candidate = Path.Combine(parent, $"{FolderName} {n}");
        return candidate;
    }

    private static string Readme(int modCount) =>
        $"""
        KH2 Rando Manager mod export
        {modCount} mod(s), plus the load order.

        What is in here
          {ModsFolderName}/           one folder per installed mod
          {OrderFileName}    the enabled mods, top of the list first

        To use this on another Mac
          1. Install KH2 Rando Manager and run Setup and Extract Game Data.
          2. Quit the app.
          3. Copy the contents of {ModsFolderName}/ into "KH2 Rando/mods/kh2" in your
             home folder, and copy {OrderFileName} into "KH2 Rando".
          4. Open the app and click Build.

        Mods are the work of their own authors. Check their terms before passing
        them on.
        """;

}
