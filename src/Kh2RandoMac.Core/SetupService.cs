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

        await InstallOptionalRuntimes(install.Bottle, workspace, log);

        WarnAboutStaleDocScripts(install.Bottle, log);

        config.Save();
    }

    /// <summary>
    /// Install the runtimes the optional features need, while the bottle is already
    /// quiet. Both are slow, bottle-exclusive installs, and leaving either until first
    /// use means meeting it at the worst moment: the tracker's when someone clicks
    /// Tracker, Re:Fined's part way through a Build, usually with the game open in
    /// both cases. Setup already demands the bottle to itself, so neither adds a
    /// restriction that was not already in force, and both are idempotent, so
    /// re-running Setup stays cheap.
    /// </summary>
    private static async Task InstallOptionalRuntimes(Bottle bottle, Workspace workspace, Action<string> log)
    {
        if (bottle.Platform != WinePlatform.CrossOver)
            return;
        await TryOptionalStep("the item tracker", log,
            () => new TrackerService().EnsureInstalled(workspace, bottle, log));
        await TryOptionalStep("Re:Fined's .NET 8 Desktop Runtime", log,
            () => new RefinedService().EnsureDesktopRuntime(workspace, bottle, log));
    }

    /// <summary>
    /// Run one optional step. Neither runtime is needed to build or play mods, so a
    /// failure here is worth a warning and nothing more; setup carries on and the
    /// feature installs on first use exactly as it did before.
    /// </summary>
    private static async Task TryOptionalStep(string what, Action<string> log, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (Exception ex)
        {
            log($"WARNING: could not set up {what} ({ex.Message}).");
            log("Modding is unaffected; it will be set up on first use instead.");
        }
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
            log("Movie folder restored, so cutscenes will play again. They crash the game under");
            log("CrossOver, so click Movies to skip them again before you next play.");
        }

        // The overlay is a bottle setting this app turned on, so leaving it would mean
        // saying the bottle is back to stock while it still shows an FPS counter.
        if (MetalHudService.IsEnabled(bottle) == true)
        {
            MetalHudService.SetEnabled(bottle, false);
            log("FPS HUD turned off.");
        }

        log("The game is back to vanilla. Mods, seeds, and extracted data were kept;");
        log("run Setup again at any time to re-enable modding.");

        // The runtimes are the one thing Reset cannot take back: uninstalling a .NET
        // from a Wine prefix is not reliable, and leaving them costs only disk.
        var runtimes = new List<string>();
        if (TrackerService.HasDotNet48(bottle))
            runtimes.Add(".NET Framework 4.8 (the item tracker)");
        if (RefinedService.HasDesktopRuntime(bottle))
            runtimes.Add(".NET 8 Desktop Runtime (Re:Fined)");
        if (runtimes.Count > 0)
            log($"Still in the bottle: {string.Join(" and ", runtimes)}. Harmless to leave; " +
                "delete the bottle in CrossOver to be rid of them.");
    }

    /// <summary>
    /// Whether this app has already changed the bottle. Any of the overrides it adds
    /// being present is enough: those are what make a bottle non-stock, and they stay
    /// behind if the user moves to a different bottle.
    /// </summary>
    public static bool HasBeenSetUp(Bottle bottle)
    {
        try
        {
            return MissingOverrides(bottle).Count < Bottle.RequiredOverrides.Length;
        }
        catch
        {
            // Unreadable registry: better to say nothing than to claim either way.
            return false;
        }
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
