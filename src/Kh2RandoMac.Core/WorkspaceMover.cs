namespace Kh2RandoMac.Core;

/// <summary>
/// Moving the workspace somewhere else. It holds the extracted game data, which is
/// about 30 GB, so where it lives matters on a Mac whose disk is smaller than the game.
/// </summary>
public static class WorkspaceMover
{
    /// <summary>
    /// Whether both paths sit on the same volume, in which case the move is a rename and
    /// finishes instantly however large the data is. Across volumes every byte is copied.
    /// </summary>
    public static bool SameVolume(string a, string b)
    {
        try
        {
            var rootA = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(a)) ?? "/").Name;
            var rootB = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(b)) ?? "/").Name;
            // Path roots are "/" for everything on macOS, so compare the mount points.
            return MountPoint(Path.GetFullPath(a)) == MountPoint(Path.GetFullPath(b)) && rootA == rootB;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The volume a path lives on, as reported by the filesystem.</summary>
    private static string MountPoint(string path)
    {
        // Walk up to something that exists: the destination may not be created yet.
        var probe = path;
        while (!Directory.Exists(probe) && Path.GetDirectoryName(probe) is { Length: > 0 } parent)
            probe = parent;
        return ShellCommand.Run("/bin/df", "-P", probe).Output
            .Split('\n').Skip(1).FirstOrDefault()?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? probe;
    }

    /// <summary>Roughly how much is there, for a warning worth reading.</summary>
    public static long SizeOnDisk(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Move a workspace. Renames when it can, copies when it must, and only removes the
    /// old copy once the new one is complete: losing a 30 GB extraction to a cable being
    /// pulled halfway through is not a recoverable afternoon.
    /// </summary>
    public static void Move(string from, string to, Action<string>? log = null)
    {
        from = Path.GetFullPath(from);
        to = Path.GetFullPath(to);
        if (!Directory.Exists(from))
            throw new InvalidOperationException($"There is nothing at '{from}' to move.");
        if (string.Equals(from, to, StringComparison.Ordinal))
            return;
        if (to.StartsWith(from + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("That folder is inside the current workspace, so it cannot hold it.");
        if (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any())
            throw new InvalidOperationException($"'{to}' already has something in it. Pick an empty folder.");

        if (SameVolume(from, to))
        {
            log?.Invoke("Same disk, so this is a rename and takes no time.");
            if (Directory.Exists(to))
                Directory.Delete(to);
            Directory.Move(from, to);
            return;
        }

        log?.Invoke($"Different disk, so {SizeOnDisk(from) / 1024 / 1024 / 1024} GB has to be copied. Leave this running.");
        DirectoryOps.Copy(from, to);
        // Only now that everything is across.
        Directory.Delete(from, true);
        log?.Invoke("Copied, and the old copy has been removed.");
    }
}
