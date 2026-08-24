namespace Kh2RandoMac.Core;

/// <summary>
/// Reversible movie-cutscene skipping. KH plays its MP4 movies through a Windows video
/// system CrossOver lacks, so movies crash the game on launch of a cutscene. The
/// community workaround: rename the movie folder, the game then skips
/// missing movies instead of crashing. KH2's movies live under
/// [STEAM|EPIC]/juefigs/KH2ReSource/zmovie.
/// </summary>
public static class MovieService
{
    private const string DisabledSuffix = ".disabled";

    /// <summary>KH2's movie folder (or its disabled twin). Null when neither exists.</summary>
    public static string? FindKh2MovieDir(string gameDir)
    {
        // Known layouts first (Steam and Epic); recursive search only as a fallback,
        // since the game tree is 70 GB.
        foreach (var launcher in new[] { "STEAM", "EPIC" })
        {
            var found = Check(Path.Combine(gameDir, launcher, "juefigs", "KH2ReSource", "zmovie"));
            if (found != null)
                return found;
        }
        foreach (var resource in Directory.EnumerateDirectories(gameDir, "KH2ReSource", SearchOption.AllDirectories))
        {
            var found = Check(Path.Combine(resource, "zmovie"));
            if (found != null)
                return found;
        }
        return null;

        static string? Check(string zmovie) =>
            Directory.Exists(zmovie) ? zmovie
            : Directory.Exists(zmovie + DisabledSuffix) ? zmovie + DisabledSuffix
            : null;
    }

    public static bool AreMoviesSkipped(string gameDir) =>
        FindKh2MovieDir(gameDir)?.EndsWith(DisabledSuffix, StringComparison.Ordinal) == true;

    /// <summary>Rename the movie folder away so the game skips all KH2 movies.</summary>
    public static void SkipMovies(string gameDir)
    {
        var dir = FindKh2MovieDir(gameDir)
            ?? throw new InvalidOperationException("KH2 movie folder not found in the game install.");
        if (dir.EndsWith(DisabledSuffix, StringComparison.Ordinal))
            return;
        Directory.Move(dir, dir + DisabledSuffix);
    }

    /// <summary>Rename the movie folder back so movies play again.</summary>
    public static void RestoreMovies(string gameDir)
    {
        var dir = FindKh2MovieDir(gameDir)
            ?? throw new InvalidOperationException("KH2 movie folder not found in the game install.");
        if (!dir.EndsWith(DisabledSuffix, StringComparison.Ordinal))
            return;
        Directory.Move(dir, dir.Substring(0, dir.Length - DisabledSuffix.Length));
    }
}
