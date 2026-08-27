namespace Kh2RandoMac.Core;

/// <summary>Copying whole mod folders around, which export and import both do.</summary>
public static class DirectoryOps
{
    public static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.Ordinal));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal), true);
    }

    /// <summary>
    /// Copy alongside the destination and swap only once the copy is complete, so a
    /// failure part way (a pulled drive, a full disk) cannot leave the caller with
    /// neither the old contents nor the new.
    /// </summary>
    public static void Replace(string source, string destination)
    {
        var staging = destination + ".importing";
        if (Directory.Exists(staging))
            Directory.Delete(staging, true);
        try
        {
            Copy(source, staging);
            if (Directory.Exists(destination))
                Directory.Delete(destination, true);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }
}
