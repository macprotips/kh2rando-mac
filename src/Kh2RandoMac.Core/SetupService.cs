namespace Kh2RandoMac.Core;

/// <summary>
/// The one true setup pipeline, shared by CLI and GUI so their behavior can't drift:
/// record the install → workspace dirs → Panacea → LuaBackend → DLL overrides → save.
/// </summary>
public class SetupService
{
    /// <summary>Runs the full setup for a chosen game install. Throws on failure; config is saved only on success.</summary>
    public async Task Run(AppConfig config, GameInstall install, Action<string> log)
    {
        config.BottleName = install.Bottle.Name;
        config.GameDir = install.GameDirMac;
        config.Launcher = install.Launcher;
        config.WrapperApp = install.Bottle.WrapperApp;
        config.Language = GameLocator.DetectLanguageFolder(install.GameDirMac) ?? "en";
        log($"Game: {config.GameDir}");
        var platform = install.Bottle.Platform == WinePlatform.Sikarugir ? "Sikarugir wrapper" : "bottle";
        log($"{char.ToUpper(platform[0])}{platform.Substring(1)}: {config.BottleName} ({config.Launcher}, language folder '{config.Language}')");

        var workspace = new Workspace(config.WorkspaceRoot);
        workspace.EnsureDirectories();

        var panacea = new PanaceaService();
        await panacea.EnsurePayload(log);
        panacea.Install(config.GameDir, install.Bottle, workspace);
        log("Panacea installed (version.dll + dependencies + panacea_settings.txt).");

        await new LuaBackendService().Install(config.GameDir, install.Bottle, workspace, config.Launcher, log);

        install.Bottle.EnsureDllOverrides(Bottle.RequiredOverrides);
        log($"Bottle DLL overrides set ({string.Join(", ", Bottle.RequiredOverrides)}).");

        WarnAboutStaleDocScripts(install.Bottle, log);

        config.Save();
    }

    /// <summary>
    /// LuaBackend also loads scripts from the game's documents folder. Leftover Lua files
    /// from an older install double-load against the built mod's scripts and are a known
    /// cause of Garden of Assemblage crashes.
    /// </summary>
    private static void WarnAboutStaleDocScripts(Bottle bottle, Action<string> log)
    {
        // The Wine user folder is "crossover" on CrossOver and the Mac username
        // elsewhere; scan every user profile in the prefix.
        var usersDir = Path.Combine(bottle.DriveC, "users");
        if (!Directory.Exists(usersDir))
            return;
        foreach (var userDir in Directory.GetDirectories(usersDir))
        {
            foreach (var rel in new[]
            {
                Path.Combine("My Games", "KINGDOM HEARTS HD 1.5+2.5 ReMIX", "scripts", "kh2"),
                Path.Combine("KINGDOM HEARTS HD 1.5+2.5 ReMIX", "scripts", "kh2"),
            })
            {
                var dir = Path.Combine(userDir, "Documents", rel);
                if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.lua").Any())
                {
                    log($"WARNING: found old Lua scripts in Documents/{rel}.");
                    log("These load alongside the mod scripts and can crash the Garden of Assemblage.");
                    log($"If the game crashes at the moogle or starts in the wrong place, empty that folder: {dir}");
                }
            }
        }
    }

    /// <summary>
    /// Undo everything setup did: remove Panacea, LuaBackend, and the DLL overrides,
    /// and restore the movie folder. The game returns to vanilla. Mods, extracted data,
    /// and the workspace are left alone so a later re-setup is quick.
    /// </summary>
    public void ResetToVanilla(AppConfig config, Action<string> log)
    {
        var gameDir = config.GameDir ?? throw new InvalidOperationException("Not set up yet, nothing to reset.");
        var bottle = Bottle.Resolve(config);
        if (!GameLocator.IsGameDir(gameDir))
            throw new InvalidOperationException("Game folder not reachable. Is the drive mounted?");

        // Registry first: it refuses while the bottle runs, and nothing should be
        // half-removed if the user needs to quit Steam and retry.
        // mscoree is added only when the tracker is set up, so it is not in the
        // required set, but Reset still has to take it away or the bottle keeps a
        // change this app made.
        bottle.RemoveDllOverrides(Bottle.RequiredOverrides.Append("mscoree"));
        log("Bottle DLL overrides removed.");

        new PanaceaService().Uninstall(gameDir);
        log("Panacea removed from the game folder.");
        var appIdFile = Path.Combine(gameDir, "steam_appid.txt");
        if (File.Exists(appIdFile))
            File.Delete(appIdFile);
        new LuaBackendService().Uninstall(gameDir);
        log("LuaBackend removed.");
        if (MovieService.AreMoviesSkipped(gameDir))
        {
            MovieService.RestoreMovies(gameDir);
            log("Movie folder restored.");
        }

        log("The game is back to vanilla. Mods, seeds, and extracted data were kept;");
        log("run Setup again at any time to re-enable modding.");
        if (TrackerService.HasDotNet48(bottle))
            log("The .NET the tracker uses is still in the bottle. It is harmless; delete the bottle in CrossOver to remove it.");
    }

    /// <summary>Names of required overrides missing from the bottle registry (empty = healthy).</summary>
    public static List<string> MissingOverrides(Bottle bottle)
    {
        var current = bottle.GetDllOverrides();
        return Bottle.RequiredOverrides
            .Where(n => !current.TryGetValue(n, out var v) || !v.Contains("native"))
            .ToList();
    }
}
