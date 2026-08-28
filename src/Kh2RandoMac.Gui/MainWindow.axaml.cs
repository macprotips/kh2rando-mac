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

        // Downloads are the long, silent part of Setup, and they are the one part that
        // can say how far along it is.
        GitHubApi.DownloadProgress += (name, done, total) =>
        {
            if (total is > 0)
                ShowProgress($"Downloading {name} \u2014 {Mb(done)} of {Mb(total.Value)} MB",
                    (double)done / total.Value);
            else
                ShowProgress($"Downloading {name} \u2014 {Mb(done)} MB");
        };

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

        SetUpBottlePicker();
        SetUpCrossOverPicker();
        _ = RefreshAllAsync();

        // Shown once, after the window is up so it has something to sit over.
        if (!_config.NoticeShown)
            Opened += async (_, _) => await ShowFirstRunNotice();
    }

    /// <summary>
    /// Said once, before anything has been changed: this app puts the bottle into a
    /// state CrossOver did not ship, which matters if they ever need CrossOver's help.
    /// </summary>
    private async Task ShowFirstRunNotice()
    {
        if (_config.NoticeShown)
            return;
        _config.NoticeShown = true;
        _config.Save();
        await NoticeAsync("Before you start",
            "This app changes your CrossOver bottle so the game can load mods. It adds a " +
            "few DLL overrides and installs the .NET runtimes the item tracker and " +
            "Re:Fined need.\n\n" +
            "That means your bottle is no longer a stock CrossOver setup. If you hit a " +
            "CrossOver problem, reproduce it in a clean version of CrossOver before asking " +
            "CodeWeavers for help.\n\n" +
            "Reset undoes all of that whenever you want, apart from the .NET runtimes, " +
            "which are harmless to leave.");
    }

    /// <summary>A message with a single acknowledging button.</summary>
    private async Task NoticeAsync(string title, string message)
    {
        var dialog = new Window
        {
            // The heading lives in the content, since these sheets do not always draw a
            // title bar; naming the window too would print it twice where they do.
            Title = "",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "Got it", FontWeight = Avalonia.Media.FontWeight.SemiBold };
        ok.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 15,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { ok },
                },
            },
        };
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Which bottle the game is modded in. Only appears when the machine has more than
    /// one, because with a single bottle there is no choice to present. Switching is not
    /// a preference: a bottle carries its own mod loader, registry overrides and
    /// runtimes, so the new one has to be set up before it can run anything.
    /// </summary>
    private void SetUpBottlePicker()
    {
        BottlePicker.SelectionChanged += OnBottleSelected;
        // The first refresh supplies the real list moments later; this only avoids the
        // row flickering into view once the window is already up.
        try { RefreshBottlePicker(Bottle.Discover()); } catch { /* refresh will retry */ }
    }

    /// <summary>
    /// Re-read the bottles on disk and repaint the menu. Run on every refresh rather
    /// than once at startup: people make and delete bottles in CrossOver while this is
    /// open, and a menu built at launch would offer bottles that no longer exist and
    /// hide the one they just made.
    /// </summary>
    private void RefreshBottlePicker(List<Bottle> found)
    {
        var names = found.Select(b => b.Name).ToList();
        var unchanged = names.SequenceEqual(_bottles.Select(b => b.Name));
        _bottles = found;
        // Always shown once there is a bottle at all, even when there is only one to
        // name. A row that appears only when a second bottle exists cannot be found by
        // anyone looking for how to change bottles, which is exactly when they look.
        BottleRow.IsVisible = found.Count > 0;
        if (unchanged && BottlePicker.ItemsSource != null)
        {
            SelectConfiguredBottle();
            return;
        }

        _switchingBottle = true;
        BottlePicker.ItemsSource = names;
        _switchingBottle = false;
        SelectConfiguredBottle();
    }

    /// <summary>
    /// Point the menu at whatever the config actually says, without that looking like a
    /// switch. Left blank when the configured bottle is not on the machine, which
    /// happens after deleting one in CrossOver: naming another would be a claim that
    /// the game is set up there.
    /// </summary>
    private void SelectConfiguredBottle()
    {
        _switchingBottle = true;
        BottlePicker.SelectedIndex = _bottles.FindIndex(b => b.Name == _config.BottleName);
        _switchingBottle = false;
    }

    private async void OnBottleSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_switchingBottle || _busy)
            return;
        var i = BottlePicker.SelectedIndex;
        if (i < 0 || i >= _bottles.Count || _bottles[i].Name == _config.BottleName)
            return;
        var target = _bottles[i];

        // Gather what the decision needs off the UI thread, then let BottleSwitch judge
        // it; the rules live there so they can be tested.
        var facts = await Task.Run(() => GatherSwitchFacts(target));
        var plan = BottleSwitch.Plan(target, facts);

        if (plan.Outcome != BottleSwitchOutcome.Ready)
        {
            await NoticeAsync(plan.Title, plan.Message);
            SelectConfiguredBottle();
            return;
        }

        if (!await ConfirmAsync(plan.Title, plan.Message, "Set Up"))
        {
            SelectConfiguredBottle();
            return;
        }

        await RunTask($"Switch to bottle '{target.Name}'", () => RunSetupFor(plan.Install!));
        SelectConfiguredBottle();
    }

    /// <summary>Read the machine for everything a bottle switch turns on.</summary>
    private BottleSwitchFacts GatherSwitchFacts(Bottle target)
    {
        var gameDir = _config.GameDir;
        var usable = gameDir != null && GameLocator.IsGameDir(gameDir);

        GameInstall? detected = null;
        var detectionFailed = false;
        if (usable)
        {
            try
            {
                var full = Path.GetFullPath(gameDir!);
                detected = GameLocator.FindAll().FirstOrDefault(g => g.Bottle.Name == target.Name
                    && Path.GetFullPath(g.GameDirMac) == full);
            }
            catch
            {
                detectionFailed = true;
            }
        }

        var leavingWasSetUp = false;
        try
        {
            leavingWasSetUp = _config.BottleName != null
                && SetupService.HasBeenSetUp(Bottle.Resolve(_config));
        }
        catch
        {
            // Current bottle gone or unreadable: nothing left behind to warn about.
        }

        return new BottleSwitchFacts(_config.BottleName, _config.Launcher, gameDir, usable,
            target.IsRunning(), target.WhatIsUsingIt(), detected, detectionFailed, leavingWasSetUp);
    }

    /// <summary>
    /// Several CrossOver copies share the same bottles, and a bottle can only be run by
    /// a copy its own age or newer. The app works out which one to use; this only
    /// appears when there is a choice to make.
    /// </summary>
    private void SetUpCrossOverPicker()
    {
        var installed = CrossOverApp.Installed();
        if (installed.Count < 2)
            return;

        // Pin the choice the first time there is more than one copy. Letting it be
        // recomputed leaves it free to change when a bottle's recorded version does,
        // and every change of copy makes CrossOver re-run its bottle update, which
        // reverts the .NET Framework the tracker depends on.
        if (_config.CrossOverAppPath == null)
        {
            try
            {
                var resolved = Bottle.Resolve(_config).OwningApp;
                if (resolved != null)
                {
                    _config.CrossOverAppPath = resolved;
                    _config.Save();
                }
            }
            catch
            {
                // Not set up yet; the picker still works, it just has nothing to pin to.
            }
        }

        var options = new List<string> { "Automatic" };
        options.AddRange(CrossOverApp.DescribeAll(installed));
        CrossOverPicker.ItemsSource = options;
        var chosen = installed.FindIndex(a => a.Path == _config.CrossOverAppPath);
        CrossOverPicker.SelectedIndex = chosen >= 0 ? chosen + 1 : 0;
        CrossOverRow.IsVisible = true;

        CrossOverPicker.SelectionChanged += (_, _) =>
        {
            var i = CrossOverPicker.SelectedIndex;
            _config.CrossOverAppPath = i <= 0 ? null : installed[i - 1].Path;
            _config.Save();
            if (i <= 0)
            {
                Log("CrossOver: automatic, matched to the bottle.");
                return;
            }
            var chosenApp = installed[i - 1];
            Log($"CrossOver: {options[i]}");

            // The only thing a switch costs the user is a few seconds on the next
            // tracker launch, while the app undoes what CrossOver puts back. Say that
            // and nothing else; the mechanism is not their problem.
            try
            {
                if (TrackerService.HasDotNet48(Bottle.Resolve(_config)))
                    Log("The tracker takes up to half a minute longer the first time you open it after this. Nothing else to do.");
            }
            catch
            {
                // Nothing set up yet, so there is nothing to disturb.
            }

            // A bottle upgraded by a newer copy cannot be opened by an older one, and
            // the failure is a cryptic "failed to load start.exe" rather than anything
            // that names the cause.
            try
            {
                var bottleVersion = Bottle.Resolve(_config).CrossOverVersion;
                if (bottleVersion != null && Version.TryParse(bottleVersion, out var needed)
                    && chosenApp.Version < needed)
                {
                    Log($"WARNING: this copy is older than your bottle (last used by {bottleVersion}), so it");
                    Log("cannot open it. Pick a newer copy, or Automatic.");
                }
            }
            catch
            {
                // Not set up yet, so there is no bottle to compare against.
            }
        };
    }

    /// <summary>Install dropped/opened mods, seeds, or folders (window drop and dock-icon drop both land here).</summary>
    public async Task InstallFilesAsync(IReadOnlyList<string> paths)
    {
        // Folders are handled first: an exported setup or a single mod folder.
        var folders = paths.Where(Directory.Exists).ToList();
        if (folders.Count > 0)
        {
            if (paths.Count > 1)
                Log($"Handling the folder '{Path.GetFileName(folders[0])}'. Drop other items separately.");
            try
            {
                await ImportFolderAsync(folders[0]);
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
                FileLog.Write(ex.ToString());
            }
            return;
        }

        var installable = paths
            .Where(p => InstallableExtensions.Any(ext => p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (installable.Count == 0)
        {
            Log("Nothing installable dropped. Drop a .zip, .kh2pcpatch, or .lua file,");
            Log("a mod folder, or a folder exported by the Export button.");
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
                WarnIfKnownIssue(name);
            }
            Log("Enabled. Click Build to apply.");
        }));
    }

    /// <summary>Import a dropped folder: an exported setup, or a single mod.</summary>
    private async Task ImportFolderAsync(string folder)
    {
        switch (ImportService.Identify(folder))
        {
            case FolderKind.SingleMod:
            {
                await RunTask($"Import {Path.GetFileName(folder)}", () => Task.Run(() =>
                {
                    var name = ImportService.ImportSingleMod(_workspace, folder, Log);
                    new ModsService(_workspace).SetEnabled(name, true);
                    Log("Enabled. Click Build to apply.");
                    WarnIfKnownIssue(name);
                }));
                return;
            }
            case FolderKind.Export:
            {
                var mods = ImportService.Preview(folder);
                var clashes = mods.Count(m => Directory.Exists(_workspace.ModPath(m)));
                var hasOrder = File.Exists(Path.Combine(folder, ExportService.OrderFileName));
                var message =
                    $"This folder holds {mods.Count} mod(s)." +
                    (clashes > 0 ? $" {clashes} of them replace a mod you already have." : "") +
                    (hasOrder
                        ? " It also carries a load order, which will replace yours. Your current order is kept as a .bak file."
                        : "");
                if (!await ConfirmAsync("Import mods", message, "Import"))
                    return;
                await RunTask("Import mods", () => Task.Run(() =>
                {
                    var count = ImportService.Import(_workspace, folder, hasOrder, Log);
                    Log($"Imported {count} mod(s). Click Build to apply.");
                    foreach (var note in KnownIssues.ForEnabled(_workspace))
                        Log("WARNING: " + note);
                }));
                return;
            }
            default:
                Log($"'{Path.GetFileName(folder)}' is not a mod folder or an export.");
                Log("A mod folder has a mod.yml in it; an export has a 'mods' folder in it.");
                return;
        }
    }

    private void Log(string message)
    {
        FileLog.Write($"[gui] {message}");
        Dispatcher.UIThread.Post(() =>
        {
            LogText.Text += $"\n{message}";
            LogScroll.ScrollToEnd();
            // Every step already announces itself in the log, so the newest line is the
            // best caption available. Warnings are skipped: they are asides, and one
            // would otherwise sit over the bar for the rest of the operation.
            if (ProgressRow.IsVisible && message.Length > 0
                && !message.StartsWith("WARNING", StringComparison.Ordinal)
                && !message.StartsWith("ERROR", StringComparison.Ordinal)
                && !message.StartsWith(" ", StringComparison.Ordinal))
            {
                ProgressLabel.Text = message.TrimStart('[').TrimEnd(']');
                TaskProgress.IsIndeterminate = true;
                ProgressPercent.Text = "";
            }
        });
    }

    private static long Mb(long bytes) => bytes / 1024 / 1024;

    /// <summary>
    /// Show the progress strip. Indeterminate unless given a fraction: most steps are
    /// installers running inside the bottle, which genuinely cannot say how far along
    /// they are, and a bar that invents a number is worse than one that admits it.
    /// </summary>
    private void ShowProgress(string label, double? fraction = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ProgressRow.IsVisible = true;
            ProgressLabel.Text = label;
            if (fraction is { } f)
            {
                TaskProgress.IsIndeterminate = false;
                TaskProgress.Value = Math.Clamp(f * 100, 0, 100);
                ProgressPercent.Text = $"{(int)(f * 100)}%";
            }
            else
            {
                TaskProgress.IsIndeterminate = true;
                ProgressPercent.Text = "";
            }
        });
    }

    private void HideProgress() => Dispatcher.UIThread.Post(() => ProgressRow.IsVisible = false);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Dispatcher.UIThread.Post(() =>
        {
            // Everything that reads or writes app state is disabled while an operation
            // runs, a stray click mid-setup or mid-build must not race the worker.
            SetupButton.IsEnabled = !busy;
            ChangeGameButton.IsEnabled = !busy;
            // Both pickers start real work, so they belong with the buttons rather than
            // staying live for a click that would only be refused.
            BottlePicker.IsEnabled = !busy;
            CrossOverPicker.IsEnabled = !busy;
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
            InstallRefinedButton.IsEnabled = !busy;
            ExportButton.IsEnabled = !busy;
            SeedGenButton.IsEnabled = !busy;
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
        // Hold the strip back briefly: removing a mod or toggling a setting finishes in
        // milliseconds, and a bar that flashes on every click reads as a glitch.
        using var settled = new CancellationTokenSource();
        _ = Task.Delay(400, settled.Token)
            .ContinueWith(_ => ShowProgress(label), settled.Token,
                TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
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
            settled.Cancel();
            SetBusy(false);
            HideProgress();
            try
            {
                await RefreshAllAsync();
            }
            catch (Exception ex)
            {
                Log($"ERROR refreshing status: {ex.Message}");
                FileLog.Write(ex.ToString());
            }
        }
    }

    /// <summary>One status row: a colored badge glyph and a short plain-words phrase.</summary>
    private record StatusRow(string Glyph, string Color, string Text)
    {
        public static StatusRow Ok(string text) => new("✓", "#66BB6A", text);
        public static StatusRow Warn(string text) => new("!", "#FFB74D", text);
        public static StatusRow Idle(string text) => new("–", "#9E9E9E", text);
    }

    private record StatusSnapshot(
        StatusRow Game, string? GamePath, StatusRow Loader, StatusRow Data, List<ModInfo> Mods,
        bool? MoviesSkipped, bool? HudEnabled, List<Bottle> Bottles);

    /// <summary>Gathers state off the UI thread (filesystem scans can block on slow drives), then paints.</summary>
    private async Task RefreshAllAsync()
    {
        var snapshot = await Task.Run(() =>
        {
            var config = AppConfig.Load();
            var workspace = new Workspace(config.WorkspaceRoot);

            var gameOk = config.GameDir != null && GameLocator.IsGameDir(config.GameDir);
            var gameRow = config.GameDir == null
                ? StatusRow.Idle("Not set up yet. Install the game in CrossOver, then click Run Setup.")
                : gameOk
                    ? StatusRow.Ok($"Found ({config.Launcher}, bottle '{config.BottleName}')")
                    : StatusRow.Warn("Game drive not connected. Plug it in and click Refresh.");

            StatusRow loaderRow;
            if (config.GameDir == null)
                loaderRow = StatusRow.Idle("Installed by Run Setup.");
            else if (!gameOk)
                loaderRow = StatusRow.Idle("Can't check while the game drive is unplugged.");
            else
            {
                var missing = new List<string>();
                if (!PanaceaService.IsInstalled(config.GameDir))
                    missing.Add("Panacea");
                if (!LuaBackendService.IsInstalled(config.GameDir))
                    missing.Add("LuaBackend");
                loaderRow = missing.Count == 0
                    ? StatusRow.Ok("Panacea and LuaBackend installed.")
                    : StatusRow.Warn($"{string.Join(" and ", missing)} missing. Click Run Setup to reinstall.");
            }

            var dataRow = !ExtractionService.LooksExtracted(workspace.DataDir)
                ? StatusRow.Idle("Not extracted yet. Click Extract Game Data after setup.")
                : gameOk && ExtractionService.IsExtractionStale(config.GameDir!, config.Language, workspace.DataDir)
                    ? StatusRow.Warn("Out of date after a game update. Run Extract Game Data, then Build.")
                    : StatusRow.Ok("Extracted.");

            var mods = new ModsService(workspace).List();
            bool? moviesSkipped = null;
            if (gameOk)
            {
                try { moviesSkipped = MovieService.AreMoviesSkipped(config.GameDir!); }
                catch { }
            }
            bool? hudEnabled = null;
            try { hudEnabled = MetalHudService.IsEnabled(Bottle.Resolve(config)); } catch { }
            // Discovery scans the bottles folder and /Applications; it belongs out here
            // with the other filesystem work, not on the UI thread during the repaint.
            List<Bottle> bottles;
            try { bottles = Bottle.Discover(); } catch { bottles = new List<Bottle>(); }

            return (config, workspace, new StatusSnapshot(gameRow, config.GameDir, loaderRow, dataRow, mods,
                moviesSkipped, hudEnabled, bottles));
        });

        _config = snapshot.config;
        _workspace = snapshot.workspace;
        var status = snapshot.Item3;

        PaintStatusRow(GameStatusBadge, GameStatusIcon, GameStatusText, status.Game);
        PaintStatusRow(LoaderStatusBadge, LoaderStatusIcon, LoaderStatusText, status.Loader);
        PaintStatusRow(DataStatusBadge, DataStatusIcon, DataStatusText, status.Data);
        RefreshBottlePicker(status.Bottles);
        GamePathText.Text = status.GamePath ?? "";
        GamePathText.IsVisible = status.GamePath != null;
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
        UpdateModCount();
    }

    /// <summary>Keep the mod tally in the hint line current as mods are toggled, moved, or removed.</summary>
    private void UpdateModCount()
    {
        var total = _mods.Count;
        var enabled = _mods.Count(m => m.Enabled);
        ModCountText.Text = total == 0
            ? "No mods installed"
            : $"{total} mod{(total == 1 ? "" : "s")} · {enabled} enabled";
    }

    private static void PaintStatusRow(Border badge, TextBlock icon, TextBlock text, StatusRow row)
    {
        var color = Avalonia.Media.Color.Parse(row.Color);
        badge.Background = new Avalonia.Media.SolidColorBrush(color, 0.22);
        icon.Text = row.Glyph;
        icon.Foreground = new Avalonia.Media.SolidColorBrush(color);
        text.Text = row.Text;
    }

    /// <summary>Persist checkbox states + current display order into mods-KH2.txt.</summary>
    private void SaveModOrder()
    {
        if (_refreshing)
            return;
        // Both files: the enabled list drives the build and is OpenKH's format, the
        // order file records where every mod sits so a disabled one keeps its place.
        _workspace.SaveModOrder(_mods.Select(m => m.Name));
        _workspace.SaveEnabledMods(_mods.Where(m => m.Enabled).Select(m => m.Name));
        UpdateModCount();
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
        GameInstall? install;
        if (installs.Count == 0)
        {
            Log("No install found automatically. If the game is on an external drive, plug it in.");
            install = await PickGameFolderAsync();
        }
        else if (installs.Count == 1)
        {
            install = installs[0];
        }
        else
        {
            // Several copies is normal once someone keeps one on an external drive, and
            // setting up the wrong one looks like the app silently doing nothing.
            var choice = await ChooseAsync("More than one copy of the game", "Pick the one to set up.",
                installs.Select(Describe).ToList());
            install = choice < 0 ? null : installs[choice];
        }

        if (install == null)
        {
            Log("Setup cancelled.");
            return;
        }
        await RunSetupFor(install);
        Log("Setup complete. Next: Extract Game Data (one time, 10-20 min).");
    });

    private async void OnChangeGameFolder(object? sender, RoutedEventArgs e) =>
        await RunTask("Change game folder", async () =>
        {
            var install = await PickGameFolderAsync();
            if (install == null)
            {
                Log("Left the game folder as it was.");
                return;
            }
            await RunSetupFor(install);
            Log("Game folder changed. Extract Game Data if this copy has not been extracted yet.");
        });

    private static string Describe(GameInstall install) =>
        $"{install.GameDirMac}\n{install.Launcher}, bottle '{install.Bottle.Name}'";

    /// <summary>
    /// Ask for the game folder by hand and work out which bottle and store it belongs
    /// to. Returns null when the user backs out of the picker.
    /// </summary>
    private async Task<GameInstall?> PickGameFolderAsync()
    {
        Log($"Pick the folder containing '{GameLocator.Kh2ExeName}'.");
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the KINGDOM HEARTS -HD 1.5+2.5 ReMIX- game folder",
            AllowMultiple = false,
        });
        var dir = picked.FirstOrDefault()?.Path.LocalPath;
        if (dir == null)
            return null;
        var install = await Task.Run(() => GameLocator.ForFolder(dir, _config.BottleName, _config.Launcher));
        Log($"Using bottle '{install.Bottle.Name}' ({install.Launcher}).");
        return install;
    }

    /// <summary>
    /// Record an install and put the mod loader in it. Setup and Change Folder are the
    /// same operation once the folder is known, so they share this.
    /// </summary>
    private async Task RunSetupFor(GameInstall install)
    {
        // Work on a local config object; the shared field is refreshed afterwards.
        var config = AppConfig.Load();
        config.WorkspaceRoot = _config.WorkspaceRoot;
        await Task.Run(() => new SetupService().Run(config, install, Log));
    }

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
            // Fires once per file, tens of thousands of times; only act when the whole
            // percent changes or every update is a cross-thread post for nothing.
            var percent = (int)(p * 100);
            if (percent == lastPercent)
                return;
            lastPercent = percent;
            ShowProgress("Extracting game data", p);
            if (percent % 5 == 0)
                Log($"  {percent}%");
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
            WarnIfKnownIssue(name);
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
        // The title bar is not drawn on these sheets, so the title has to live in the
        // content or the dialog never says what it is about.
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var cancel = new Button { Content = "Cancel" };
        var ok = new Button { Content = continueText, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        cancel.Click += (_, _) => dialog.Close();
        ok.Click += (_, _) => { result = true; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children =
            {
                heading,
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

    /// <summary>
    /// Ask which of several items to use. Returns the chosen index, or -1 if cancelled.
    /// </summary>
    private async Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> options)
    {
        var chosen = -1;
        var dialog = new Window
        {
            Title = title,
            Width = 540,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        // As with ConfirmAsync, no title bar is drawn, so the title has to be content.
        var heading = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var list = new ListBox { ItemsSource = options, SelectedIndex = 0, MaxHeight = 240 };
        var cancel = new Button { Content = "Cancel" };
        var ok = new Button { Content = "Use This One", FontWeight = Avalonia.Media.FontWeight.SemiBold };
        cancel.Click += (_, _) => dialog.Close();
        ok.Click += (_, _) => { chosen = list.SelectedIndex; dialog.Close(); };
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children =
            {
                heading,
                text,
                list,
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
        return chosen;
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
            Log("Run Setup first so the app knows which bottle to change.");
            return;
        }
        var current = MetalHudService.IsEnabled(bottle);
        if (current == null)
        {
            Log("The FPS HUD toggle works with CrossOver bottles only.");
            return;
        }
        // Check before asking, as everywhere else: a refusal after the dialog reads as
        // the button having done nothing.
        if (bottle.IsRunning())
        {
            await NoticeAsync("Something is using the bottle",
                "Changing the FPS HUD writes a bottle setting, and CrossOver would " +
                "overwrite it on the way out.\n\n" +
                $"{bottle.WhatIsUsingIt()}, then click FPS HUD again.");
            return;
        }

        var turningOn = current != true;
        var action = turningOn ? "Turn On" : "Turn Off";
        var confirmed = await ConfirmAsync(
            turningOn ? "Turn on the FPS HUD" : "Turn off the FPS HUD",
            (turningOn
                ? "The FPS HUD is a frame rate overlay shown while you play. "
                : "This removes the frame rate overlay shown while you play. ") +
            "It applies to your Kingdom Hearts bottle only.\n\n" +
            "Quit CrossOver and Steam before continuing. Start them again afterwards and the " +
            (turningOn ? "overlay will be there." : "overlay will be gone."),
            action);
        if (!confirmed)
            return;
        try
        {
            MetalHudService.SetEnabled(bottle, turningOn);
            HudButton.Content = turningOn ? "FPS HUD: On" : "FPS HUD: Off";
            Log(turningOn
                ? "FPS HUD on for this bottle. Start Steam and the game to see it."
                : "FPS HUD off for this bottle. Start Steam and the game to see it gone.");
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
        }
    }

    private bool _switchingBottle;
    private List<Bottle> _bottles = new();
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
            Log("Run Setup first so the app knows which bottle to run the tracker in.");
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
                "The tracker crashed on startup, which usually means the .NET Framework in your " +
                "Kingdom Hearts bottle is incomplete. Repair reinstalls it, which can take ten " +
                "minutes. Quit the game and Steam first.",
                "Repair");
            _trackerRepairArmed = false;
            if (!repair)
                return;
            await RunTask("Repair tracker install", () => Task.Run(() =>
                new TrackerService().EnsureInstalled(_workspace, bottle, Log, force: true)));
            if (TrackerService.IsInstalled(_workspace, bottle))
                await LaunchTrackerWithSpinner(bottle);
            return;
        }

        // CrossOver reinstates its own .NET whenever it sets the bottle up again, which
        // it does after any version change. Undo that here rather than launching into a
        // crash and asking the user to run a repair: it takes seconds.
        if (TrackerService.IsInstalled(_workspace, bottle))
        {
            var ready = true;
            await RunTask("Prepare the tracker", () => Task.Run(() =>
                ready = TrackerService.PrepareForLaunch(bottle, Log)));
            if (!ready)
            {
                Log("Could not clear it. Quit the game and Steam, then click Tracker again.");
                return;
            }
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

        if (bottle.IsRunning())
        {
            await NoticeAsync("Something is using the bottle",
                "Installing the tracker's .NET needs the bottle to itself.\n\n" +
                $"{bottle.WhatIsUsingIt()}, then click Tracker again.");
            Log($"{bottle.WhatIsUsingIt()}, then click Tracker again.");
            return;
        }

        var confirmed = await ConfirmAsync(
            "Install the item tracker",
            "This downloads the KH2 item tracker and installs the .NET Framework it needs " +
            "into your Kingdom Hearts bottle. The .NET step happens once and can take ten " +
            "minutes. Quit the game and Steam first.",
            "Install");
        if (!confirmed)
            return;
        await RunTask("Install tracker", () => Task.Run(() =>
            new TrackerService().EnsureInstalled(_workspace, bottle, Log)));
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
            Log("Starting the tracker. Its window can take up to a minute to appear.");
            if (await TrackerService.WaitUntilRunning(proc, TimeSpan.FromSeconds(60)))
            {
                Log("Tracker started. If its window is not up yet, give it a moment. Turn on");
                Log("auto-tracking from its Options menu once the game is running.");
            }
            else if (proc.HasExited)
            {
                _trackerRepairArmed = true;
                Log("The tracker exited without showing a window; it crashed while starting.");
                Log("Click Tracker again to run a repair install (up to ten minutes; quit Steam first).");
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

    private async void OnExportMods(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        if (_mods.Count == 0)
        {
            Log("Nothing to export yet, no mods are installed.");
            return;
        }

        string size;
        try
        {
            size = ExportService.DescribeSize(await Task.Run(() => ExportService.EstimateSize(_workspace)));
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            return;
        }
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Choose an empty folder for the export ({size})",
            AllowMultiple = false,
        });
        var destination = picked.FirstOrDefault()?.Path.LocalPath;
        if (destination == null)
            return;

        await RunTask("Export mods", () => Task.Run(() =>
        {
            var created = ExportService.Export(_workspace, destination, Log);
            Log($"Exported {_mods.Count} mod(s) and the load order to {created}");
            Log("Zip that one folder to share it, or drop it on this window to restore it later.");
        }));
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
    private void OnMoveToTop(object? sender, RoutedEventArgs e) => MoveModTo(sender, 0);
    private void OnMoveToBottom(object? sender, RoutedEventArgs e) => MoveModTo(sender, _mods.Count - 1);

    /// <summary>Move a mod to an absolute position (long lists make one-step nudging tedious).</summary>
    private void MoveModTo(object? sender, int target)
    {
        if (_busy || (sender as Control)?.Tag is not ModRow row)
            return;
        var index = _mods.IndexOf(row);
        if (index < 0 || target < 0 || target >= _mods.Count || index == target)
            return;
        _mods.Move(index, target);
        SaveModOrder();
    }

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
            try
            {
                System.Diagnostics.Process.Start("/usr/bin/open", new[] { found });
            }
            catch (Exception ex)
            {
                Log($"ERROR: could not open the Seed Generator: {ex.Message}");
            }
            Log("Opening the Seed Generator. Generate a seed, then drag the zip onto this window.");
            return;
        }

        // Not installed, run the bundled installer script, streaming its output here.
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "Resources", "seedgen-setup", "setup-seed-generator.sh"));
        if (!File.Exists(script))
        {
            Log("This copy of the app is missing its Seed Generator installer, which means");
            Log("the download was incomplete. Re-download the app from the releases page.");
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

    private async void OnInstallRefined(object? sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_workspace.ModPath(RefinedService.MainMod)))
        {
            Log("Re:Fined is already installed; tick it in the list and Build.");
            return;
        }
        var download = await ConfirmAsync(
            "Install Re:Fined",
            "Re:Fined adds quality-of-life features: skippable cutscenes, soft reset, " +
            "a prologue skip, and faster menus. Use it on its own or alongside a randomizer " +
            "seed. It is a large download, so give it a few minutes.",
            "Download");
        if (!download)
            return;
        await RunTask("Install Re:Fined", () => Task.Run(() =>
        {
            var mods = new ModsService(_workspace);
            var name = mods.InstallFromGit(RefinedService.MainMod, Log);
            mods.SetEnabled(name, true);
            Log("Enabled. Click Build to apply; the first Build offers a one-time");
            Log(".NET runtime install into the bottle, which Re:Fined runs on.");
            Log("For a normal playthrough, untick your seed and GoA first. For a rando");
            Log("run with Re:Fined, keep it between your seed and GoA in the list.");
        }));
    }

    /// <summary>Surface a confirmed problem with a mod at the moment the user acts on it.</summary>
    private void WarnIfKnownIssue(string modName)
    {
        var note = KnownIssues.For(modName);
        if (note != null)
            Log("WARNING: " + note);
    }

    private void BuildCore()
    {
        if (!ExtractionService.LooksExtracted(_workspace.DataDir))
            throw new InvalidOperationException("Game data not extracted yet, click Extract Game Data first.");
        foreach (var note in KnownIssues.ForEnabled(_workspace))
            Log("WARNING: " + note);
        new PatchBuilder(_workspace).Build(Log, _config.Language);
    }

    private async void OnBuild(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureRefinedPrerequisites())
            return;
        await RunTask("Build", () => Task.Run(() =>
        {
            BuildCore();
            Log("Build complete ✓. Launch the game through CrossOver, then start a New Game.");
        }));
    }

    private async void OnBuildRun(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureRefinedPrerequisites())
            return;
        await RunTask("Build & Run", () => Task.Run(() =>
        {
            BuildCore();
            Log("Build complete ✓, launching the game via CrossOver...");
            Log(Core.Launcher.LaunchKh2(_config));
        }));
    }

    /// <summary>
    /// Re:Fined needs the .NET 8 Desktop Runtime inside the bottle; offer the one-time
    /// install when a Re:Fined mod is enabled. Also warn when the randomizer mods are
    /// enabled alongside it: the two rewrite the same game systems and do not mix.
    /// Returns false when the build should not proceed yet.
    /// </summary>
    private async Task<bool> EnsureRefinedPrerequisites()
    {
        if (_busy || !RefinedService.AnyRefinedEnabled(_workspace))
            return !_busy;

        var conflicts = RefinedService.ConflictingEnabledMods(_workspace);
        if (conflicts.Count > 0)
        {
            Log("Note: Re:Fined and the randomizer are enabled together. This combo is common");
            Log("(soft reset, skippable cutscenes) and works; place Re:Fined between your seed");
            Log("and GoA. Known quirk: seeds with 'Remove Port Royal Map Select' get glitchy");
            Log("Port Royal menus under Re:Fined.");
        }

        Bottle bottle;
        try
        {
            bottle = Bottle.Resolve(_config);
        }
        catch
        {
            return true; // Not set up yet; the build itself reports that properly.
        }
        if (bottle.Platform != WinePlatform.CrossOver || RefinedService.HasDesktopRuntime(bottle))
            return true;

        // Check before asking, not after. Told afterwards, in the log, the refusal
        // reads as the button having done nothing, and people simply click again.
        if (bottle.IsRunning())
        {
            await NoticeAsync("Something is using the bottle",
                "Re:Fined needs the .NET 8 Desktop Runtime installed into your Kingdom Hearts " +
                "bottle, and that needs the bottle to itself.\n\n" +
                $"{bottle.WhatIsUsingIt()}, then click Build again.");
            Log($"Build stopped. {bottle.WhatIsUsingIt()}, then click Build again.");
            return false;
        }

        var confirmed = await ConfirmAsync(
            "Install the .NET 8 Desktop Runtime",
            "Re:Fined needs the .NET 8 Desktop Runtime, which is not in your Kingdom " +
            "Hearts bottle yet. This is a one-time install of a few minutes. " +
            "Quit the game and Steam first.",
            "Install");
        if (!confirmed)
        {
            Log("Build cancelled: Re:Fined needs the .NET 8 Desktop Runtime in the bottle.");
            return false;
        }
        var ok = true;
        await RunTask("Install .NET 8 Desktop Runtime", async () =>
        {
            try
            {
                await Task.Run(() => new RefinedService().EnsureDesktopRuntime(_workspace, bottle, Log));
            }
            catch
            {
                ok = false;
                throw;
            }
        });
        return ok;
    }
}
