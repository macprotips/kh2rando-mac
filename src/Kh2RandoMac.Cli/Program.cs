using Kh2RandoMac.Core;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var rest = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "setup" => await Setup(),
        "status" => Status(),
        "extract" => await Extract(),
        "install" => Install(rest),
        "remove" => ModAction(rest, "remove"),
        "update" => Update(rest),
        "enable" => ModAction(rest, "enable"),
        "disable" => ModAction(rest, "disable"),
        "list" => ListMods(),
        "build" => Build(),
        "mode" => Mode(rest),
        "run" => Run(),
        "movies" => Movies(rest),
        "tracker" => await Tracker(),
        "reset" => Reset(),
        "panacea" => await Panacea(rest),
        "luabackend" => await LuaBackend(rest),
        "overrides" => Overrides(),
        "help" or "--help" or "-h" => Help(),
        _ => Unknown(command),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine($"(details logged to {FileLog.LogPath})");
    FileLog.Write($"[cli {command}] {ex}");
    return 1;
}

static void Say(string message)
{
    Console.WriteLine(message);
    FileLog.Write($"[cli] {message}");
}

static int Help()
{
    Console.WriteLine("""
        kh2rando, KH2 Randomizer mod manager for macOS + CrossOver

        Setup:
          setup                     Detect CrossOver bottle + game, create workspace, install
                                    Panacea + LuaBackend, set DLL overrides
          extract                   Extract KH2 game data (needed once, ~10-20 min, ~30 GB)
          status                    Show configuration and health checks

        Mods:
          install <author/repo>     Install a mod from GitHub (e.g. KH2FM-Mods-Num/GoA-ROM-Edition)
          install <file>            Install a seed/mod zip, .kh2pcpatch, or standalone .lua
          list                      List installed mods (top of list wins conflicts)
          enable <mod>              Enable a mod (moves it to the top of the load order)
          disable <mod>             Disable a mod
          remove <mod>              Uninstall a mod
          update [mod]              Update GitHub-installed mods (no argument = all)
          build                     Build enabled mods into the folder Panacea loads
                                    (no mods enabled = clean vanilla build)
          mode [rando|refined]      Show or switch the play mode; each mode keeps its
                                    own enabled-mod list (Re:Fined = QoL overhaul)

        Game:
          run                       Launch KH 1.5+2.5 through CrossOver
          movies [skip|restore]     Skip KH2 movie cutscenes (they crash the game under
                                    CrossOver) or restore them
          tracker                   Open the KH2 item tracker next to the game
                                    (first run installs it, 15-30 min once)
          panacea install|remove    Manage the in-game mod loader DLLs
          luabackend install|remove Manage LuaBackend (Lua script support)
          overrides                 Re-apply the bottle DLL overrides
          reset                     Return the game to vanilla (removes Panacea, LuaBackend,
                                    and the bottle changes; keeps mods and extracted data)

        Typical flow: setup → extract → install <mod/seed> → build → run.
        After changing seeds/mods, run build again before playing.
        """);
    return 0;
}

static int Unknown(string cmd)
{
    Console.Error.WriteLine($"Unknown command '{cmd}'. Run 'kh2rando help'.");
    return 1;
}

static (AppConfig, Workspace) LoadConfigured()
{
    var config = AppConfig.Load();
    if (config.BottleName == null || config.GameDir == null)
        throw new InvalidOperationException("Not configured yet, run 'kh2rando setup' first.");
    return (config, new Workspace(config.WorkspaceRoot));
}

static async Task<int> Setup()
{
    var config = AppConfig.Load();

    Say("Searching CrossOver bottles and Sikarugir wrappers for KINGDOM HEARTS HD 1.5+2.5 ReMIX...");
    var installs = GameLocator.FindAll();
    GameInstall install;
    if (installs.Count == 0)
    {
        Say("No install found automatically. If the game is on an external drive, plug it in and retry.");
        Console.Write("Enter the mac path of the game folder (the one containing 'KINGDOM HEARTS II FINAL MIX.exe'): ");
        var dir = Console.ReadLine()?.Trim().Trim('"', '\'') ?? "";
        if (!GameLocator.IsGameDir(dir))
            throw new InvalidOperationException($"'{dir}' does not contain {GameLocator.Kh2ExeName}. " +
                "If the game is on an external drive, make sure it is mounted.");
        var bottles = Bottle.Discover();
        var owner = bottles.FirstOrDefault(b => dir.StartsWith(b.Root, StringComparison.Ordinal))
            ?? PickBottle(bottles);
        Console.Write("Launcher [Steam/EGS] (default Steam): ");
        install = new GameInstall(owner, dir, AppConfig.NormalizeLauncher(Console.ReadLine()));
    }
    else
    {
        for (var i = 0; i < installs.Count; i++)
            Console.WriteLine($"  [{i + 1}] {installs[i].GameDirMac}  (bottle: {installs[i].Bottle.Name}, {installs[i].Launcher})");
        var pick = 0;
        if (installs.Count > 1)
        {
            Console.Write($"Pick install [1-{installs.Count}]: ");
            pick = Math.Clamp(int.Parse(Console.ReadLine() ?? "1") - 1, 0, installs.Count - 1);
        }
        install = installs[pick];
    }

    await new SetupService().Run(config, install, Say);
    Say("Setup complete. Next: 'kh2rando extract' (one-time, 10-20 min).");
    return 0;
}

static Bottle PickBottle(List<Bottle> bottles)
{
    if (bottles.Count == 0)
        throw new InvalidOperationException("No CrossOver bottles or Sikarugir wrappers found.");
    Console.WriteLine("Which bottle does the game run in?");
    for (var i = 0; i < bottles.Count; i++)
        Console.WriteLine($"  [{i + 1}] {bottles[i].Name}");
    Console.Write($"Pick bottle [1-{bottles.Count}]: ");
    var pick = Math.Clamp(int.Parse(Console.ReadLine() ?? "1") - 1, 0, bottles.Count - 1);
    return bottles[pick];
}

static int Status()
{
    var config = AppConfig.Load();
    var workspace = new Workspace(config.WorkspaceRoot);
    Console.WriteLine($"Config file:  {AppPaths.ConfigFile}");
    Console.WriteLine($"Bottle:       {config.BottleName ?? "(not set, run setup)"}");
    Console.WriteLine($"Game folder:  {config.GameDir ?? "(not set)"}");
    Console.WriteLine($"Launcher:     {config.Launcher}");
    Console.WriteLine($"Workspace:    {config.WorkspaceRoot}");

    if (config.GameDir != null)
    {
        var gameOk = GameLocator.IsGameDir(config.GameDir);
        Console.WriteLine($"Game reachable:      {(gameOk ? "yes" : "NO, is the drive mounted?")}");
        if (gameOk)
        {
            Console.WriteLine($"Panacea installed:   {(PanaceaService.IsInstalled(config.GameDir) ? "yes" : "no")}");
            Console.WriteLine($"LuaBackend installed:{(LuaBackendService.IsInstalled(config.GameDir) ? " yes" : " no")}");
        }
    }
    if (config.BottleName != null)
    {
        Bottle? bottle = null;
        try { bottle = Bottle.Resolve(config); } catch { }
        if (bottle != null)
        {
            var missing = SetupService.MissingOverrides(bottle);
            Console.WriteLine($"DLL overrides:       {(missing.Count == 0 ? "ok" : "MISSING: " + string.Join(", ", missing) + ", run 'kh2rando overrides'")}");
        }
    }
    var extracted = ExtractionService.LooksExtracted(workspace.DataDir);
    var stale = extracted && config.GameDir != null && GameLocator.IsGameDir(config.GameDir)
        && ExtractionService.IsExtractionStale(config.GameDir, config.Language, workspace.DataDir);
    Console.WriteLine($"Game data extracted: {(!extracted ? "no, run 'kh2rando extract'" : stale ? "STALE (game updated), run 'kh2rando extract' again" : "yes")}");
    var enabled = workspace.EnabledMods();
    Console.WriteLine($"Enabled mods:        {(enabled.Count == 0 ? "(none)" : string.Join(", ", enabled))}");
    return 0;
}

static async Task<int> Extract()
{
    var (config, workspace) = LoadConfigured();
    if (!GameLocator.IsGameDir(config.GameDir!))
        throw new InvalidOperationException($"Game folder not reachable: {config.GameDir}. Is the drive mounted?");
    if (ExtractionService.LooksExtracted(workspace.DataDir))
        Say("Game data already extracted, re-extracting will overwrite it.");

    Say($"Extracting KH2 data to {workspace.GameDataDir} (this takes 10-20 minutes)...");
    await new ExtractionService().ExtractKh2(config.GameDir!, config.Language, workspace.DataDir,
        PercentReporter(Say));
    Say("Extraction complete.");
    return 0;
}

// Throttles float progress into "every 5%" log lines.
static Action<float> PercentReporter(Action<string> log)
{
    var last = -1;
    return p =>
    {
        var percent = (int)(p * 100);
        if (percent != last && percent % 5 == 0)
        {
            last = percent;
            log($"  {percent}%");
        }
    };
}

static int Install(string[] rest)
{
    if (rest.Length == 0)
        throw new InvalidOperationException("Usage: kh2rando install <author/repo | seed.zip | file.kh2pcpatch | script.lua>");
    var (_, workspace) = LoadConfigured();
    workspace.EnsureDirectories();
    var mods = new ModsService(workspace);
    var target = rest[0];
    string name;
    if (File.Exists(target))
    {
        name = target.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? mods.InstallFromLua(target, Say)
            : mods.InstallFromZip(target, Say);
    }
    else
    {
        name = mods.InstallFromGit(target, Say);
    }
    mods.SetEnabled(name, true);
    Say($"Enabled '{name}'. Run 'kh2rando build' to apply.");
    return 0;
}

static int ModAction(string[] rest, string action)
{
    if (rest.Length == 0)
        throw new InvalidOperationException($"Usage: kh2rando {action} <mod>");
    var (_, workspace) = LoadConfigured();
    var mods = new ModsService(workspace);
    switch (action)
    {
        case "remove":
            mods.Remove(rest[0]);
            Say($"Removed {rest[0]}.");
            break;
        case "enable":
            mods.SetEnabled(rest[0], true);
            Say($"Enabled {rest[0]} (top of load order). Run 'kh2rando build' to apply.");
            break;
        case "disable":
            mods.SetEnabled(rest[0], false);
            Say($"Disabled {rest[0]}. Run 'kh2rando build' to apply.");
            break;
    }
    return 0;
}

static int Update(string[] rest)
{
    var (_, workspace) = LoadConfigured();
    var mods = new ModsService(workspace);
    if (rest.Length > 0)
    {
        mods.Update(rest[0], Say);
        Say("Run 'kh2rando build' to apply.");
        return 0;
    }
    Say("Checking installed mods for updates...");
    var updates = mods.CheckForUpdates(Say);
    if (updates.Count == 0)
    {
        Say("Everything is up to date.");
        return 0;
    }
    foreach (var (name, behind) in updates)
    {
        Say($"{name}: {behind} new commit(s)");
        mods.Update(name, Say);
    }
    Say("Run 'kh2rando build' to apply.");
    return 0;
}

static int Movies(string[] rest)
{
    var (config, _) = LoadConfigured();
    if (!GameLocator.IsGameDir(config.GameDir!))
        throw new InvalidOperationException("Game folder not reachable. Is the drive mounted?");
    switch (rest.FirstOrDefault())
    {
        case "skip":
            MovieService.SkipMovies(config.GameDir!);
            Say("Movies skipped. The game now skips KH2 cutscenes instead of crashing on them.");
            break;
        case "restore":
            MovieService.RestoreMovies(config.GameDir!);
            Say("Movies restored. Cutscenes will play again, and will crash the game under CrossOver.");
            break;
        default:
            Console.WriteLine($"Movies: {(MovieService.AreMoviesSkipped(config.GameDir!) ? "skipped" : "on")} " +
                "(use 'kh2rando movies skip|restore')");
            break;
    }
    return 0;
}

static async Task<int> Tracker()
{
    var (config, workspace) = LoadConfigured();
    var bottle = Bottle.Resolve(config);
    if (!TrackerService.IsInstalled(workspace, bottle))
    {
        Say("Tracker not installed yet; installing. The one-time .NET Framework step");
        Say("takes 15 to 30 minutes. Quit the game and Steam in CrossOver first.");
        await new TrackerService().EnsureInstalled(workspace, bottle, Say);
    }
    if (TrackerService.NeedsRuntimePin(bottle))
    {
        TrackerService.PinRuntime(bottle);
        Say("One-time fix applied: bottle pinned to the real .NET Framework.");
    }
    var proc = TrackerService.Launch(workspace, bottle);
    Say("Tracker starting...");
    if (await TrackerService.WaitUntilVisible(TimeSpan.FromSeconds(60)))
        Say("Tracker is up. In its Options menu, auto-tracking connects once the game is running.");
    else if (proc.HasExited)
    {
        Say("The tracker exited without showing a window; it crashed while starting.");
        Say($"Details were saved to {FileLog.LogPath}; attach that file to a bug report.");
    }
    else
        Say("The tracker is taking longer than usual; its window should appear shortly.");
    return 0;
}

static int ListMods()
{
    var (_, workspace) = LoadConfigured();
    var list = new ModsService(workspace).List();
    if (list.Count == 0)
    {
        Console.WriteLine("No mods installed.");
        return 0;
    }
    foreach (var mod in list)
    {
        var mark = mod.Enabled ? "[x]" : "[ ]";
        var title = mod.Metadata?.Title;
        Console.WriteLine($"  {mark} {mod.Name}{(title != null && title != mod.Name ? $"  ({title})" : "")}");
    }
    Console.WriteLine("\nEnabled mods load top-first; run 'kh2rando build' after changes.");
    return 0;
}

static int Mode(string[] rest)
{
    var (config, workspace) = LoadConfigured();
    var current = ModeService.Normalize(config.ActiveMode);
    if (rest.Length == 0)
    {
        Say($"Current mode: {current}. 'kh2rando mode rando' or 'kh2rando mode refined' switches.");
        return 0;
    }
    if (ModeService.Normalize(rest[0]) == current)
    {
        Say($"Already in {current} mode.");
        return 0;
    }
    var next = ModeService.Switch(config, workspace);
    config.Save();
    Say($"Mode: {next}. Run 'kh2rando build' to apply. Keep separate save slots per mode.");
    return 0;
}

static int Build()
{
    var (config, workspace) = LoadConfigured();
    if (!ExtractionService.LooksExtracted(workspace.DataDir))
        throw new InvalidOperationException("Game data not extracted yet, run 'kh2rando extract' first.");
    if (RefinedService.AnyRefinedEnabled(workspace))
    {
        var conflicts = RefinedService.ConflictingEnabledMods(workspace);
        if (conflicts.Count > 0)
        {
            Say("WARNING: Re:Fined and other gameplay mods are enabled together: " + string.Join(", ", conflicts));
            Say("Re:Fined and the randomizer do not mix. Enable one or the other, then build.");
        }
        var bottle = Bottle.Resolve(config);
        if (bottle.Platform == WinePlatform.CrossOver && !RefinedService.HasDesktopRuntime(bottle))
        {
            Say("Re:Fined needs the .NET 8 Desktop Runtime in the bottle; installing (one time).");
            new RefinedService().EnsureDesktopRuntime(workspace, bottle, Say).GetAwaiter().GetResult();
        }
    }
    new PatchBuilder(workspace).Build(Say, config.Language);
    Say("Done. Launch the game through CrossOver (or 'kh2rando run').");
    return 0;
}

static int Run()
{
    var (config, _) = LoadConfigured();
    Say(Launcher.LaunchKh2(config));
    return 0;
}

static async Task<int> Panacea(string[] rest)
{
    var (config, workspace) = LoadConfigured();
    var bottle = Bottle.Resolve(config);
    var service = new PanaceaService();
    if (rest.FirstOrDefault() == "remove")
    {
        service.Uninstall(config.GameDir!);
        Say("Panacea removed from the game folder.");
    }
    else
    {
        await service.EnsurePayload(Say);
        service.Install(config.GameDir!, bottle, workspace);
        Say("Panacea installed.");
    }
    return 0;
}

static async Task<int> LuaBackend(string[] rest)
{
    var (config, workspace) = LoadConfigured();
    var bottle = Bottle.Resolve(config);
    var service = new LuaBackendService();
    if (rest.FirstOrDefault() == "remove")
    {
        service.Uninstall(config.GameDir!);
        Say("LuaBackend removed.");
    }
    else
    {
        await service.Install(config.GameDir!, bottle, workspace, config.Launcher, Say);
    }
    return 0;
}

static int Reset()
{
    var (config, _) = LoadConfigured();
    Console.WriteLine("This returns the game to vanilla: removes Panacea, LuaBackend, and the bottle");
    Console.WriteLine("DLL overrides, and restores the movie folder. Mods, seeds, and extracted data");
    Console.WriteLine("are kept. Quit Steam and the game in CrossOver first.");
    Console.Write("Type yes to continue: ");
    if (Console.ReadLine()?.Trim().ToLowerInvariant() != "yes")
    {
        Console.WriteLine("Cancelled.");
        return 1;
    }
    new SetupService().ResetToVanilla(config, Say);
    return 0;
}

static int Overrides()
{
    var (config, _) = LoadConfigured();
    var bottle = Bottle.Resolve(config);
    bottle.EnsureDllOverrides(Bottle.RequiredOverrides);
    Say($"DLL overrides applied ({string.Join(", ", Bottle.RequiredOverrides)} → native,builtin).");
    return 0;
}
