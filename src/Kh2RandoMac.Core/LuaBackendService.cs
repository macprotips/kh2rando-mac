using System.IO.Compression;

namespace Kh2RandoMac.Core;

/// <summary>
/// Installs LuaBackend (Sirius902/LuaBackend) into the game folder: DBGHELP.dll from the
/// release renamed to LuaBackend.dll (Panacea chain-loads it), plus LuaBackend.toml with
/// the compiled mod scripts folder added as a bottle-visible Windows path.
/// </summary>
public class LuaBackendService
{
    public async Task Install(string gameDir, Bottle bottle, Workspace workspace, string launcher, Action<string>? progress = null)
    {
        progress?.Invoke("Fetching latest LuaBackend release...");
        var release = await GitHubApi.GetLatestRelease("Sirius902", "LuaBackend");
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault()
            ?? throw new InvalidOperationException("LuaBackend release has no assets.");

        var zipPath = Path.Combine(AppPaths.CacheDir, "LuaBackend.zip");
        progress?.Invoke($"Downloading LuaBackend {release.Tag}...");
        await GitHubApi.DownloadFile(asset.DownloadUrl, zipPath);

        var tempDir = Path.Combine(AppPaths.CacheDir, "LuaBackend");
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, true);
        ZipFile.ExtractToDirectory(zipPath, tempDir);

        var dll = Directory.GetFiles(tempDir, "DBGHELP.dll", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("DBGHELP.dll not found in LuaBackend release zip.");
        var toml = Directory.GetFiles(tempDir, "LuaBackend.toml", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("LuaBackend.toml not found in LuaBackend release zip.");

        File.Copy(dll, Path.Combine(gameDir, "LuaBackend.dll"), true);

        var scriptsPathWin = ExpectedScriptsPath(bottle, workspace);
        var config = PatchToml(File.ReadAllText(toml), scriptsPathWin, launcher == "Steam");
        File.WriteAllText(Path.Combine(gameDir, "LuaBackend.toml"), config);
        Directory.Delete(tempDir, true);
        File.Delete(zipPath);
        progress?.Invoke("LuaBackend installed.");
    }

    /// <summary>
    /// Patch the stock LuaBackend.toml: add the built mod scripts folder to the [kh2]
    /// scripts list, and for Steam installs swap game_docs to the "My Games/" variant
    /// (the stock file ships EGS-active, Steam commented out). Mirrors Mods Manager.
    /// </summary>
    public static string PatchToml(string toml, string scriptsPathWin, bool steam)
    {
        var config = toml.Replace("\\\\", "/").Replace("\\", "/");
        var kh2Section = config.IndexOf("[kh2]", StringComparison.Ordinal);
        if (kh2Section < 0)
            throw new InvalidOperationException("LuaBackend.toml has no [kh2] section.");
        var insertAt = config.IndexOf("true }", kh2Section, StringComparison.Ordinal);
        if (insertAt < 0)
            throw new InvalidOperationException("Unexpected LuaBackend.toml scripts format.");
        insertAt += "true }".Length;
        config = config.Insert(insertAt, ", {path = \"" + scriptsPathWin.Replace('\\', '/') + "\", relative = false}");

        if (steam)
        {
            // Only touch [kh2]'s own lines, an unbounded search would leak into the next
            // section (e.g. [bbs]) if kh2's needles are missing.
            var section = config.IndexOf("[kh2]", StringComparison.Ordinal);
            var sectionEnd = config.IndexOf("\n[", section, StringComparison.Ordinal);
            if (sectionEnd < 0)
                sectionEnd = config.Length;
            var egs = config.IndexOf("game_docs = \"KINGDOM HEARTS HD 1.5+2.5 ReMIX\"", section, StringComparison.Ordinal);
            var steamIdx = config.IndexOf("# game_docs = \"My Games/KINGDOM HEARTS HD 1.5+2.5 ReMIX\"", section, StringComparison.Ordinal);
            if (egs < 0 || steamIdx < 0 || egs >= sectionEnd || steamIdx >= sectionEnd)
                throw new InvalidOperationException(
                    "LuaBackend.toml's [kh2] section doesn't have the expected game_docs lines, " +
                    "the LuaBackend release format may have changed. Please report this.");
            config = config.Remove(steamIdx, 2);
            config = config.Insert(egs, "# ");
        }
        return config;
    }

    /// <summary>
    /// The scripts folder LuaBackend will actually read, written into the game folder at
    /// setup. Moving the files leaves it reading from nowhere, which breaks every Lua
    /// mod, the Garden of Assemblage included, while everything else looks installed.
    /// </summary>
    public static bool ScriptsPathMatches(string gameDir, string expectedScriptsPathWin)
    {
        var toml = Path.Combine(gameDir, "LuaBackend.toml");
        if (!File.Exists(toml))
            return true; // Not installed; reported separately rather than twice.
        try
        {
            var wanted = expectedScriptsPathWin.Replace('\\', '/').TrimEnd('/');
            return File.ReadAllText(toml)
                .Replace('\\', '/')
                .Contains(wanted, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true; // Unreadable: do not raise an alarm on a guess.
        }
    }

    /// <summary>The scripts path setup records, for comparing against what is there.</summary>
    public static string ExpectedScriptsPath(Bottle bottle, Workspace workspace) =>
        bottle.ToWindowsPath(Path.Combine(workspace.CompiledModRoot, "kh2", "scripts")).Replace('\\', '/');

    public static bool IsInstalled(string gameDir) =>
        File.Exists(Path.Combine(gameDir, "LuaBackend.dll")) && File.Exists(Path.Combine(gameDir, "LuaBackend.toml"));

    public void Uninstall(string gameDir)
    {
        foreach (var f in new[] { "LuaBackend.dll", "LuaBackend.toml" })
        {
            var path = Path.Combine(gameDir, f);
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
