namespace Kh2RandoMac.Core;

/// <summary>
/// How much space folders take. Uses du rather than walking the tree in .NET: the
/// extracted game data alone is tens of thousands of files, and du answers in a second
/// where enumerating every FileInfo takes considerably longer.
/// </summary>
public static class DiskUsage
{
    /// <summary>
    /// Sizes in bytes, keyed by the path asked for. One du call for the lot, since the
    /// mod list asks about twenty-odd folders at once and twenty processes to answer
    /// one question is twenty times the cost.
    /// </summary>
    public static Dictionary<string, long> Of(IReadOnlyList<string> paths)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        var existing = paths.Where(Directory.Exists).ToList();
        if (existing.Count == 0)
            return result;

        var args = new List<string> { "-sk" };
        args.AddRange(existing);
        var output = ShellCommand.Run("/usr/bin/du", TimeSpan.FromSeconds(60), args.ToArray()).Output;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "<kilobytes>\t<path>", and paths contain spaces, so split on the tab only.
            var tab = line.IndexOf('\t');
            if (tab <= 0 || !long.TryParse(line[..tab].Trim(), out var kb))
                continue;
            result[line[(tab + 1)..]] = kb * 1024;
        }
        return result;
    }

    public static long Of(string path) =>
        Of(new[] { path }).TryGetValue(path, out var size) ? size : 0;

    /// <summary>A size someone can read at a glance, not an exact byte count.</summary>
    public static string Human(long bytes)
    {
        if (bytes <= 0)
            return "";
        double value = bytes;
        foreach (var unit in new[] { "KB", "MB", "GB" })
        {
            value /= 1024;
            if (value < 1024 || unit == "GB")
                return value >= 10 || unit == "KB"
                    ? $"{value:0} {unit}"
                    : $"{value:0.0} {unit}";
        }
        return $"{value:0} GB";
    }
}
