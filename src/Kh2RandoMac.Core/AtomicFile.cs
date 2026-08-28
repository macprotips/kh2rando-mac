namespace Kh2RandoMac.Core;

/// <summary>
/// Writing a file by staging beside it and renaming into place, so an interrupted write
/// cannot leave a truncated file where a complete one was. Used for the files that would
/// cost someone real work to lose: the bottle's registry, CrossOver's bottle config, and
/// this app's own settings. A rename within a directory either happens or does not, so
/// the reader sees the old file or the new one and never half of either.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllLines(string path, IEnumerable<string> lines) =>
        Write(path, tmp => File.WriteAllLines(tmp, lines));

    public static void WriteAllText(string path, string text) =>
        Write(path, tmp => File.WriteAllText(tmp, text));

    private static void Write(string path, Action<string> writeTo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Unique per call. Two writes sharing one staging name is how a half-written
        // file gets renamed into place looking complete, which is what cost this app's
        // own settings once already.
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            writeTo(tmp);
            File.Move(tmp, path, true);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }
}
