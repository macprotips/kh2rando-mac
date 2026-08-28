using System.Collections.Concurrent;
using OpenKh.Patcher;

namespace Kh2RandoMac.Core;

/// <summary>
/// Builds the compiled mod folder from the enabled mods, the native equivalent of
/// Mods Manager's "Build". Mods are applied bottom-to-top so the top of the list wins.
/// </summary>
public class PatchBuilder
{
    private readonly Workspace _workspace;

    public PatchBuilder(Workspace workspace)
    {
        _workspace = workspace;
    }

    /// <param name="languageFolder">The game's Image/&lt;folder&gt; name; only "jp" changes patcher behavior.</param>
    public void Build(Action<string>? progress = null, string languageFolder = "en")
    {
        // Validate everything BEFORE wiping the previous build, so a bad state can't
        // destroy a working mod folder and leave nothing behind.
        var mods = new ModsService(_workspace).List().Where(m => m.Enabled).ToList();
        foreach (var mod in mods)
        {
            if (mod.Metadata?.Assets == null)
                throw new InvalidOperationException(
                    $"Mod '{mod.Name}' has a missing or unreadable mod.yml, fix or remove it, then build again.");
            if (mod.Metadata.IsCollection)
                progress?.Invoke(
                    $"WARNING: '{mod.Name}' is a collection mod; optional add-on selection isn't supported yet, " +
                    "only its always-on assets will be built.");
        }

        if (Bottle.IsGameRunning())
            progress?.Invoke("WARNING: Kingdom Hearts is running. This replaces the mods it is reading; " +
                "quit the game and build again if it misbehaves.");

        if (Directory.Exists(_workspace.CompiledModDir))
        {
            try
            {
                Directory.Delete(_workspace.CompiledModDir, true);
            }
            catch (Exception ex)
            {
                progress?.Invoke($"Warning: could not fully clean the mod directory: {ex.Message}");
            }
        }
        Directory.CreateDirectory(_workspace.CompiledModDir);

        if (mods.Count == 0)
            progress?.Invoke("No mods enabled, building an empty (vanilla) mod folder.");

        var patcher = new PatcherProcessor();
        var packageMap = new ConcurrentDictionary<string, string>();

        for (var i = mods.Count - 1; i >= 0; i--)
        {
            var mod = mods[i];
            progress?.Invoke($"Building {mod.Metadata!.Title ?? mod.Name}...");
            NormalizePathSeparators(mod.Metadata.Assets);
            patcher.Patch(
                _workspace.GameDataDir,
                _workspace.CompiledModDir,
                mod.Metadata,
                mod.Path,
                platform: 2, // PC
                fastMode: false,
                packageMap: packageMap,
                LaunchGame: Workspace.Game,
                Language: languageFolder == "jp" ? "jp" : "en",
                Tests: false,
                collectionOptionalEnabledMods: new Dictionary<string, bool>());
        }

        using var writer = new StreamWriter(Path.Combine(_workspace.CompiledModDir, "patch-package-map.txt"));
        foreach (var entry in packageMap)
            writer.WriteLine(entry.Key + " $$$$ " + entry.Value);

        progress?.Invoke($"Build complete: {mods.Count} mod(s) → {_workspace.CompiledModDir}");
    }

    /// <summary>
    /// Mod definitions written on Windows use backslash paths (obj\FILE.mdlx); many mix
    /// them with forward slashes in the same file. Windows treats both as separators,
    /// macOS treats a backslash as an ordinary character, so those assets quietly fail
    /// to resolve and the patcher skips them. Normalize every asset path up front.
    /// </summary>
    public static void NormalizePathSeparators(List<OpenKh.Patcher.AssetFile>? assets)
    {
        if (assets == null)
            return;
        foreach (var asset in assets)
        {
            if (asset == null)
                continue;
            if (asset.Name != null)
                asset.Name = asset.Name.Replace('\\', '/');
            if (asset.Multi != null)
                foreach (var multi in asset.Multi)
                    if (multi?.Name != null)
                        multi.Name = multi.Name.Replace('\\', '/');
            NormalizePathSeparators(asset.Source);
        }
    }
}
