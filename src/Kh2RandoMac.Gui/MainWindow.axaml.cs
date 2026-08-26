using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Kh2RandoMac.Core;

namespace Kh2RandoMac.Gui;

public class ModRow : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string Title { get; init; }

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            EnabledChanged?.Invoke();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public Action? EnabledChanged { get; set; }
}

public partial class MainWindow : Window
{
    private AppConfig _config = AppConfig.Load();
    private Workspace _workspace;
    private readonly ObservableCollection<ModRow> _mods = new();
    private bool _busy;
    private bool _refreshing;

    private static readonly string[] InstallableExtensions = { ".zip", ".kh2pcpatch", ".lua" };

    public MainWindow()
    {
        InitializeComponent();
        FileLog.Write($"[gui] KH2 Rando Manager build {AppInfo.Build} started");
        VersionText.Text = $"Version {AppInfo.Build}";
        _workspace = new Workspace(_config.WorkspaceRoot);
        _workspace.EnsureDirectories();
        ModList.ItemsSource = _mods;

        // Mods and seeds can be dragged straight onto the window (dock-icon drops
        // arrive separately via file activation in App).
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.Data.Contains(DataFormats.Files)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var paths = e.Data.GetFiles()
                ?.Select(f => f.TryGetLocalPath())
                .Where(p => p != null)
                .Select(p => p!)
                .ToList() ?? new List<string>();
            await InstallFilesAsync(paths);
        });

        _ = RefreshAllAsync();
    }

    /// <summary>Install dropped/opened mod or seed files (window drop and dock-icon drop both land here).</summary>
    public async Task InstallFilesAsync(IReadOnlyList<string> paths)
    {
        var installable = paths
            .Where(p => InstallableExtensions.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (installable.Count == 0)
        {
            Log("No installable files dropped. Supported: .zip, .kh2pcpatch, .lua");
            return;
        }
        await RunTask($"Install {installable.Count} file(s)", () => Task.Run(() =>
        {
            var mods = new ModsService(_workspace);
            foreach (var file in installable)
            {
                var name = file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                    ? mods.InstallFromLua(file, Log)
                    : mods.InstallFromZip(file, Log);
                mods.SetEnabled(name, true);
            }
            Log("Enabled. Click Build to apply.");
        }));
    }

    private void Log(string message)
    {
        FileLog.Write($"[gui] {message}");
        Dispatcher.UIThread.Post(() =>
        {
            LogText.Text += $"\n{message}";
            LogScroll.ScrollToEnd();
        });
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Dispatcher.UIThread.Post(() =>
        {
            // Everything that reads or writes app state is disabled while an operation
            // runs, a stray click mid-setup or mid-build must not race the worker.
            SetupButton.IsEnabled = !busy;
            ExtractButton.IsEnabled = !busy;
            BuildButton.IsEnabled = !busy;
            RunButton.IsEnabled = !busy;
            RefreshButton.IsEnabled = !busy;
            UpdatesButton.IsEnabled = !busy;
            ResetButton.IsEnabled = !busy;
            MoviesButton.IsEnabled = !busy;
            HudButton.IsEnabled = !busy;
            TrackerButton.IsEnabled = !busy;
            InstallGitButton.IsEnabled = !busy;
            InstallZipButton.IsEnabled = !busy;
            InstallGoaButton.IsEnabled = !busy;
            ModList.IsEnabled = !busy;
        });
    }

    private async Task RunTask(string label, Func<Task> work)
    {
        if (_busy)
        {
            Log("Busy with another operation. Wait for it to finish.");
            return;
        }
        SetBusy(true);
        Log($"[{label}]");
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            Log($"(full details in {FileLog.LogPath})");
            FileLog.Write(ex.ToString());
        }
        finally
        {
            SetBusy(false);
            await RefreshAllAsync();
        }
    }

    private record StatusSnapshot(
        string GameStatus, string LoaderStatus, string DataStatus, List<ModInfo> Mods,
        bool? MoviesSkipped, bool? HudEnabled);

    /// <summary>Gathers state off the UI thread (filesystem scans can block on slow drives), then paints.</summary>
    private async Task RefreshAllAsync()
    {
        var snapshot = await Task.Run(() =>
        {
            var config = AppConfig.Load();
            var workspace = new Workspace(config.WorkspaceRoot);

            var gameOk = config.GameDir != null && GameLocator.IsGameDir(config.GameDir);
            var gameStatus = config.GameDir == null
                ? "Game: not set up yet, click Run Setup (game must be installed in CrossOver or Sikarugir first)"
                : gameOk
                    ? $"Game: {config.GameDir}  ({config.Launcher}, bottle '{config.BottleName}')"
                    : $"Game: {config.GameDir}, NOT REACHABLE (drive unplugged?)";

            var loaderStatus = config.GameDir != null && gameOk
                ? $"Mod loader: Panacea {(PanaceaService.IsInstalled(config.GameDir) ? "installed" : "MISSING")}, " +
                  $"LuaBackend {(LuaBackendService.IsInstalled(config.GameDir) ? "installed" : "MISSING")}"
                : "Mod loader: (waiting for setup)";

            var dataStatus = !ExtractionService.LooksExtracted(workspace.DataDir)
                ? "Game data: not extracted yet, click Extract Game Data after setup"
                : gameOk && ExtractionService.IsExtractionStale(config.GameDir!, config.Language, workspace.DataDir)
                    ? "Game data: STALE (the game was updated). Run Extract Game Data again, then Build."
                    : "Game data: extracted ✓";

            var mods = new ModsService(workspace).List();
            bool? moviesSkipped = null;
            if (gameOk)
            {
                try { moviesSkipped = MovieService.AreMoviesSkipped(config.GameDir!); }
                catch { }
            }
            bool? hudEnabled = null;
            try { hudEnabled = MetalHudService.IsEnabled(Bottle.Resolve(config)); } catch { }
            return (config, workspace, new StatusSnapshot(gameStatus, loaderStatus, dataStatus, mods, moviesSkipped,
                hudEnabled));
        });

        _config = snapshot.config;
        _workspace = snapshot.workspace;
        var status = snapshot.Item3;

        GameStatusText.Text = status.GameStatus;
        LoaderStatusText.Text = status.LoaderStatus;
        DataStatusText.Text = status.DataStatus;
        MoviesButton.Content = status.MoviesSkipped == true ? "Movies: Skipped" : "Movies: On";
        MoviesButton.IsEnabled = status.MoviesSkipped != null && !_busy;
        HudButton.Content = status.HudEnabled == true ? "FPS HUD: On" : "FPS HUD: Off";
        HudButton.IsEnabled = status.HudEnabled != null && !_busy;
        if ((DateTime.Now - _resetArmedAt).TotalSeconds > 10)
            ResetButton.Content = "Reset…";


        _refreshing = true;
        _mods.Clear();
        foreach (var mod in status.Mods)
        {
            var row = new ModRow
            {
                Name = mod.Name,
                Title = mod.Metadata?.Title ?? mod.Name,
                Enabled = mod.Enabled,
            };
            row.EnabledChanged = SaveModOrder;
            _mods.Add(row);
        }
        _refreshing = false;
    }

    /// <summary>Persist checkbox states + current display order into mods-KH2.txt.</summary>
    private void SaveModOrder()
    {
        if (_refreshing)
            return;
        _workspace.SaveEnabledMods(_mods.Where(m => m.Enabled).Select(m => m.Name));
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (!_busy)
            await RefreshAllAsync();
    }

    private async void OnSetup(object? sender, RoutedEventArgs e) => await RunTask("Setup", async () =>
    {
        Log("Searching CrossOver bottles and Sikarugir wrappers for KINGDOM HEARTS HD 1.5+2.5 ReMIX...");
        var installs = await Task.Run(GameLocator.FindAll);
        GameInstall install;
        if (installs.Count == 0)
        {
            Log("No install found automatically. If the game is on an external drive, plug it in.");
            Log("Otherwise, pick the game folder by hand (the one containing 'KINGDOM HEARTS II FINAL MIX.exe').");
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the KINGDOM HEARTS -HD 1.5+2.5 ReMIX- game folder",
                AllowMultiple = false,
            });
            var dir = picked.FirstOrDefault()?.Path.LocalPath;
            if (dir == null)
            {
                Log("Setup cancelled.");
                return;
            }
            if (!GameLocator.IsGameDir(dir))
                throw new InvalidOperationException($"That folder has no {GameLocator.Kh2ExeName}, wrong folder?");
            var bottles = Bottle.Discover();
            var owner = bottles.FirstOrDefault(b => dir.StartsWith(b.Root, StringComparison.Ordinal)) ?? bottles.FirstOrDefault()
                ?? throw new InvalidOperationException("No CrossOver bottles or Sikarugir wrappers found.");
            install = new GameInstall(owner, dir, "Steam");
            Log($"Using bottle '{owner.Name}'.");
        }
        else
        {
            install = installs[0];
            if (installs.Count > 1)
                Log($"Found {installs.Count} installs; using the first: {install.GameDirMac}");
        }

        // Work on a local config object; the shared field is refreshed afterwards.
        var config = AppConfig.Load();
        config.WorkspaceRoot = _config.WorkspaceRoot;
        await Task.Run(() => new SetupService().Run(config, install, Log));
        Log("Setup complete. Next: Extract Game Data (one time, 10-20 min).");
    });

    private async void OnExtract(object? sender, RoutedEventArgs e) => await RunTask("Extract game data", async () =>
    {
        if (_config.GameDir == null)
            throw new InvalidOperationException("Run Setup first.");
        if (!GameLocator.IsGameDir(_config.GameDir))
            throw new InvalidOperationException("Game folder not reachable, is the drive plugged in?");
        Log("Extracting KH2 data, this takes 10-20 minutes. Leave the app open.");
        var lastPercent = -1;
        await new ExtractionService().ExtractKh2(_config.GameDir, _config.Language, _workspace.DataDir, p =>
        {
            var percent = (int)(p * 100);
            if (percent != lastPercent && percent % 5 == 0)
            {
                lastPercent = percent;
                Log($"  {percent}%");
            }
        });
        Log("Extraction complete ✓");
    });

    private async void OnInstallGit(object? sender, RoutedEventArgs e)
    {
        var repo = RepoBox.Text?.Trim();
        if (string.IsNullOrEmpty(repo))
        {
            Log("Type a GitHub mod first, e.g. KH2FM-Mods-Num/GoA-ROM-Edition");
            return;
        }
        await RunTask($"Install {repo}", () => Task.Run(() =>
        {
            var mods = new ModsService(_workspace);
            var name = mods.InstallFromGit(repo, Log);
            mods.SetEnabled(name, true);
            Log($"Enabled '{name}'. Click Build to apply.");
        }));
        RepoBox.Text = "";
    }

    private async void OnInstallZip(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a mod, randomizer seed, .kh2pcpatch, or .lua script",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("KH2 mods") { Patterns = new[] { "*.zip", "*.kh2pcpatch", "*.lua" } },
            },
        });
        var file = picked.FirstOrDefault()?.Path.LocalPath;
        if (file == null)
            return;
        await InstallFilesAsync(new[] { file });
    }

    private DateTime _resetArmedAt = DateTime.MinValue;

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        // Two-click confirm: arm on the first click, execute only if the second click
        // comes within ten seconds.
        if ((DateTime.Now - _resetArmedAt).TotalSeconds > 10)
        {
            _resetArmedAt = DateTime.Now;
            ResetButton.Content = "Confirm Reset";
            Log("Reset returns the game to vanilla: removes the mod loader, LuaBackend, and the");
            Log("bottle changes, and restores movies. Mods, seeds, and extracted data are kept.");
            Log("Quit Steam and the game first, then click Confirm Reset within 10 seconds.");
            return;
        }
        _resetArmedAt = DateTime.MinValue;
        ResetButton.Content = "Reset…";
        await RunTask("Reset to vanilla", () => Task.Run(() =>
            new SetupService().ResetToVanilla(_config, Log)));
    }

    private async void OnCheckUpdates(object? sender, RoutedEventArgs e) => await RunTask("Check for mod updates", () => Task.Run(() =>
    {
        var mods = new ModsService(_workspace);
        var updates = mods.CheckForUpdates(Log);
        if (updates.Count == 0)
        {
            Log("Everything is up to date.");
            return;
        }
        foreach (var (name, behind) in updates)
        {
            Log($"{name}: {behind} new commit(s)");
            mods.Update(name, Log);
        }
        Log("Updated. Click Build to apply.");
    }));

    private async void OnToggleMovies(object? sender, RoutedEventArgs e) => await RunTask("Toggle movies", () => Task.Run(() =>
    {
        if (_config.GameDir == null || !GameLocator.IsGameDir(_config.GameDir))
            throw new InvalidOperationException("Game folder not reachable.");
        if (MovieService.AreMoviesSkipped(_config.GameDir))
        {
            MovieService.RestoreMovies(_config.GameDir);
            Log("Movies restored. Cutscenes will play again, and will crash the game under CrossOver.");
        }
        else
        {
            MovieService.SkipMovies(_config.GameDir);
            Log("Movies skipped. The game now skips KH2 cutscenes instead of crashing on them. Toggle again to restore.");
        }
    }));

    /// <summary>Small modal with a message and Continue/Cancel. Returns true on Continue.</summary>
    private async Task<bool> ConfirmAsync(string title, string message, string continueText)
    {
        var result = false;
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var cancel = new Button { Content = "Cancel" };
        var ok = new Button { Content = continueText, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        cancel.Click += (_, _) => dialog.Close();
        ok.Click += (_, _) => { result = true; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                text,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { cancel, ok },
                },
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }

    private async void OnToggleHud(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        Bottle bottle;
        try
        {
            bottle = Bottle.Resolve(_config);
        }
        catch
        {
            Log("Run Setup first; the FPS HUD is a per-bottle setting.");
            return;
        }
        var current = MetalHudService.IsEnabled(bottle);
        if (current == null)
        {
            Log("The FPS HUD toggle works with CrossOver bottles only.");
            return;
        }
        var turningOn = current != true;
        var confirmed = await ConfirmAsync(
            turningOn ? "Turn on the FPS HUD" : "Turn off the FPS HUD",
            $"This changes the '{bottle.Name}' bottle only; nothing else on the Mac is affected. " +
            "Quit CrossOver and Steam completely first, then relaunch them, the HUD change " +
            "only applies to programs started after it.",
            turningOn ? "Turn On" : "Turn Off");
        if (!confirmed)
            return;
        try
        {
            MetalHudService.SetEnabled(bottle, turningOn);
            HudButton.Content = turningOn ? "FPS HUD: On" : "FPS HUD: Off";
            Log(turningOn
                ? "FPS HUD on for this bottle. Relaunch CrossOver and Steam, then start the game to see it."
                : "FPS HUD off for this bottle. Relaunch CrossOver and Steam for it to disappear.");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
        }
    }

    private bool _trackerLaunching;
    private bool _trackerRepairArmed;

    private async void OnTracker(object? sender, RoutedEventArgs e)
    {
        if (_busy || _trackerLaunching)
            return;
        Bottle bottle;
        try
        {
            bottle = Bottle.Resolve(_config);
        }
        catch
        {
            Log("Run Setup first; the tracker runs inside the game's bottle.");
            return;
        }
        if (bottle.Platform != WinePlatform.CrossOver)
        {
            Log("The tracker currently works with CrossOver bottles only.");
            return;
        }
        TrackerService.LogDotNetState(bottle);

        if (_trackerRepairArmed)
        {
            var repair = await ConfirmAsync(
                "Repair the tracker",
                "The tracker crashed last time, which usually means the bottle's .NET Framework " +
                "is incomplete. Repair removes Wine's substitute and reinstalls the real " +
                "framework, which takes 15 to 30 minutes. Quit the game and Steam in CrossOver " +
                "before continuing.",
                "Repair");
            if (!repair)
                return;
            _trackerRepairArmed = false;
            await RunTask("Repair tracker install", () =>
                new TrackerService().EnsureInstalled(_workspace, bottle, Log, force: true));
            if (TrackerService.IsInstalled(_workspace, bottle))
                await LaunchTrackerWithSpinner(bottle);
            return;
        }

        if (TrackerService.IsInstalled(_workspace, bottle))
        {
            if (TrackerService.NeedsRuntimePin(bottle))
            {
                try
                {
                    TrackerService.PinRuntime(bottle);
                    Log("One-time fix applied: bottle pinned to the real .NET Framework.");
                }
                catch (Exception ex)
                {
                    Log($"ERROR: {ex.Message}");
                    return;
                }
            }
            await LaunchTrackerWithSpinner(bottle);
            return;
        }

        var confirmed = await ConfirmAsync(
            "Install the item tracker",
            "This downloads the community KH2 tracker (Dee-Ayy/KH2Tracker) and installs " +
            $".NET Framework 4.8 into the '{bottle.Name}' bottle, which the tracker needs to run. " +
            "The .NET install happens once and takes 15 to 30 minutes. " +
            "Quit the game and Steam in CrossOver before continuing.",
            "Install");
        if (!confirmed)
            return;
        await RunTask("Install tracker", () =>
            new TrackerService().EnsureInstalled(_workspace, bottle, Log));
        if (TrackerService.IsInstalled(_workspace, bottle))
            await LaunchTrackerWithSpinner(bottle);
    }

    /// <summary>
    /// Launch the tracker and keep the button in a "Launching…" spinner state until its
    /// window is actually on screen (Wine takes 10 to 20 seconds to start it). Other
    /// buttons stay usable during the wait.
    /// </summary>
    private async Task LaunchTrackerWithSpinner(Bottle bottle)
    {
        _trackerLaunching = true;
        var original = TrackerButton.Content;
        TrackerButton.IsEnabled = false;
        TrackerButton.Content = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new ProgressBar
                {
                    IsIndeterminate = true,
                    Width = 40,
                    Height = 5,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = "Launching…",
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                },
            },
        };
        try
        {
            var proc = TrackerService.Launch(_workspace, bottle);
            Log("Launching the tracker...");
            if (await TrackerService.WaitUntilVisible(TimeSpan.FromSeconds(60)))
                Log("Tracker is up. In its Options menu, auto-tracking connects once the game is running.");
            else if (proc.HasExited)
            {
                _trackerRepairArmed = true;
                Log("The tracker exited without showing a window; it crashed while starting.");
                Log("Click Tracker again to run a repair install (15 to 30 minutes; quit Steam first).");
                Log("Details were saved to the app log, now highlighted in Finder.");
                RevealLogInFinder();
            }
            else
                Log("The tracker is taking longer than usual; its window should appear shortly.");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            FileLog.Write(ex.ToString());
        }
        finally
        {
            _trackerLaunching = false;
            TrackerButton.Content = original;
            TrackerButton.IsEnabled = !_busy;
        }
    }

    private void OnRevealLog(object? sender, RoutedEventArgs e) => RevealLogInFinder();

    /// <summary>Highlight the log file in Finder so it can be dragged into a bug report.</summary>
    private void RevealLogInFinder()
    {
        try
        {
            if (!File.Exists(FileLog.LogPath))
                FileLog.Write("Log file created from the Log button.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/open")
            {
                ArgumentList = { "-R", FileLog.LogPath },
            });
        }
        catch (Exception ex)
        {
            Log($"Could not open Finder: {ex.Message}. The log is at {FileLog.LogPath}");
        }
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e) => MoveMod(sender, -1);
    private void OnMoveDown(object? sender, RoutedEventArgs e) => MoveMod(sender, +1);

    private void MoveMod(object? sender, int delta)
    {
        if (_busy || (sender as Control)?.Tag is not ModRow row)
            return;
        var index = _mods.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _mods.Count)
            return;
        _mods.Move(index, target);
        SaveModOrder();
    }

    private async void OnRemoveMod(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not ModRow row)
            return;
        await RunTask($"Remove {row.Name}", () => Task.Run(() =>
        {
            new ModsService(_workspace).Remove(row.Name);
            Log($"Removed {row.Name}.");
        }));
    }

    private async void OnOpenSeedGenerator(object? sender, RoutedEventArgs e)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, "Desktop", "KH2 Seed Generator.app"),
            Path.Combine(home, "Applications", "KH2 Seed Generator.app"),
            "/Applications/KH2 Seed Generator.app",
        };
        var found = candidates.FirstOrDefault(Directory.Exists);
        if (found != null)
        {
            System.Diagnostics.Process.Start("/usr/bin/open", new[] { found });
            Log("Opening the Seed Generator. Generate a seed, then drag the zip onto this window.");
            return;
        }

        // Not installed, run the bundled installer script, streaming its output here.
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "Resources", "seedgen-setup", "setup-seed-generator.sh"));
        if (!File.Exists(script))
        {
            Log("Seed Generator not installed yet. Run 'bash tools/setup-seed-generator.sh' from");
            Log("the project folder (see docs/SETUP.md, Part 5).");
            return;
        }
        await RunTask("Install Seed Generator (a few minutes)", () => Task.Run(() =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(script);
            using var process = System.Diagnostics.Process.Start(psi)!;
            process.OutputDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) Log(args.Data); };
            process.ErrorDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) Log(args.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Installer did not finish, see the messages above.");
            Log("Seed Generator installed, it's on your Desktop. Click this button again to open it.");
        }));
    }

    private async void OnInstallGoa(object? sender, RoutedEventArgs e)
    {
        const string goa = "KH2FM-Mods-Num/GoA-ROM-Edition";
        if (Directory.Exists(_workspace.ModPath(goa)))
        {
            Log("Garden of Assemblage is already installed.");
            return;
        }
        await RunTask("Install Garden of Assemblage", () => Task.Run(() =>
        {
            var mods = new ModsService(_workspace);
            var name = mods.InstallFromGit(goa, Log);
            mods.SetEnabled(name, true);
            Log("Enabled. Click Build to apply.");
        }));
    }

    private void BuildCore()
    {
        if (!ExtractionService.LooksExtracted(_workspace.DataDir))
            throw new InvalidOperationException("Game data not extracted yet, click Extract Game Data first.");
        new PatchBuilder(_workspace).Build(Log, _config.Language);
    }

    private async void OnBuild(object? sender, RoutedEventArgs e) => await RunTask("Build", () => Task.Run(() =>
    {
        BuildCore();
        Log("Build complete ✓, launch the game through CrossOver whenever you like.");
    }));

    private async void OnBuildRun(object? sender, RoutedEventArgs e) => await RunTask("Build & Run", () => Task.Run(() =>
    {
        BuildCore();
        Log("Build complete ✓, launching the game via CrossOver...");
        Log(Core.Launcher.LaunchKh2(_config));
    }));
}
