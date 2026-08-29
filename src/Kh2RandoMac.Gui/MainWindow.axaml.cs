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

    /// <summary>
    /// A confirmed problem with this mod, shown on its row. A warning only at build
    /// time scrolls past among forty other lines: this app warned one user about a
    /// game-freezing mod on three consecutive builds and they never saw it.
    /// </summary>
    public string? Issue { get; init; }

    public bool HasIssue => Issue != null;

    /// <summary>Disk size, shown beside the mod's name; blank when it could not be read.</summary>
    public string? Size { get; init; }

    /// <summary>The dim second line: "author/repo" plus the size when there is one.</summary>
    public string Subtitle => string.IsNullOrEmpty(Size) ? Name : $"{Name}  ·  {Size}";

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
            // Refuse the drag outright while an operation runs, so the pointer says no
            // on the way in. Accepting it and then declining leaves someone believing
            // they dropped mods in that were never installed.
            e.DragEffects = !_busy && e.Data.Contains(DataFormats.Files)
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

        // Claimed before anything reads or writes the workspace; the notice on Opened
        // shuts this copy down again if another already holds it.
        _otherInstancePid = SingleInstance.OtherInstancePid();
        if (_otherInstancePid == null)
        {
            SingleInstance.Claim();
            Closed += (_, _) => SingleInstance.Release();
        }

        SetUpBottlePicker();
        SetUpCrossOverPicker();
        _ = RefreshAllAsync();

        // Opens the way it was left. Saved on the way out rather than on every drag,
        // which would write the config continuously while someone resizes.
        Opened += (_, _) => RestoreLayout();
        Closing += (_, _) => SaveLayout();

        Opened += async (_, _) =>
        {
            if (_otherInstancePid != null)
            {
                await NoticeAsync("Already running",
                    "Another copy of KH2 Rando Manager is open, and two copies sharing one " +
                    "settings file and mod folder is how settings get lost.\n\n" +
                    "This window will close. Use the one already open, and delete any older " +
                    "copies of the app you have kept.");
                Close();
                return;
            }
            if (!_config.NoticeShown)
                await ShowFirstRunNotice();
        };
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

    /// <summary>
    /// Tell the game where the mods went. Only setup writes that path, so a move has to
    /// run it again or the game keeps loading from the old folder and quietly applies
    /// nothing.
    /// </summary>
    private async Task RepointGameAtMovedFilesAsync()
    {
        if (_config.GameDir == null || !GameLocator.IsGameDir(_config.GameDir)
            || !PanaceaService.IsInstalled(_config.GameDir))
            return; // Nothing set up to re-point; Run Setup will do it when they get there.

        try
        {
            var install = await Task.Run(() =>
                GameLocator.ForFolder(_config.GameDir!, _config.BottleName, _config.Launcher));
            await RunSetupFor(install);
            Log("The game has been pointed at the new location.");
        }
        catch (Exception ex)
        {
            Log($"WARNING: the files moved, but the game could not be told where they went ({ex.Message}).");
            Log("Mods will not apply until you click Run Setup.");
        }
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
                    Log("The tracker takes up to half a minute longer the first time you open it after this. Nothing else is needed.");
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
        // Dropping on the window is refused by the drag itself, but a drop on the dock
        // icon arrives here directly. Either way this has to say so: a line in the log
        // is not enough for something someone physically did and expects to see happen.
        if (_busy)
        {
            var what = paths.Count == 1 ? "That file was" : $"Those {paths.Count} items were";
            await NoticeAsync("Busy with something else",
                $"{what} not installed. The app is part way through another operation and " +
                "will not start a second one.\n\nDrop them again once it has finished.");
            return;
        }

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
                var clashes = mods.Count(m => _workspace.IsModInstalled(m));
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
            // Follow the newest line only for a reader already at the bottom; someone
            // who scrolled up to read an earlier message must not be yanked back down
            // by every new line. Judged before the append, while the extent still
            // describes what they were looking at.
            var wasAtEnd = LogScroll.Offset.Y + LogScroll.Viewport.Height
                >= LogScroll.Extent.Height - 24;
            LogText.Text += $"\n{message}";
            // After layout, not now: scrolled in the same pass as the append, the
            // extent is still the old text's, so the view stops one line short.
            if (wasAtEnd)
                Dispatcher.UIThread.Post(LogScroll.ScrollToEnd, DispatcherPriority.Background);
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

    /// <summary>Which row of the window holds the log; the splitter moves it.</summary>
    private const int LogRowIndex = 5;

    /// <summary>
    /// Put the window back to the size it was left at, and the log back to the height it
    /// was dragged to. Clamped to the screen actually in use: a size saved on a large
    /// display must not open a window taller than a small one.
    /// </summary>
    private void RestoreLayout()
    {
        try
        {
            if (_config.WindowWidth is > 0 && _config.WindowHeight is > 0)
            {
                var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea;
                var scale = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.Scaling ?? 1;
                var maxWidth = area != null ? area.Value.Width / scale : double.MaxValue;
                var maxHeight = area != null ? area.Value.Height / scale : double.MaxValue;
                Width = Math.Clamp(_config.WindowWidth.Value, MinWidth, Math.Max(MinWidth, maxWidth));
                Height = Math.Clamp(_config.WindowHeight.Value, MinHeight, Math.Max(MinHeight, maxHeight));
            }

            if (_config.LogHeight is > 0)
                RootGrid.RowDefinitions[LogRowIndex].Height = new GridLength(SaneLogHeight(_config.LogHeight.Value));
        }
        catch (Exception ex)
        {
            // A remembered layout is a convenience; never let it stop the window opening.
            FileLog.Write($"[gui] could not restore the layout: {ex.Message}");
        }
    }

    private void SaveLayout()
    {
        // The duplicate copy closes itself moments after opening; its size is not a
        // choice anyone made.
        if (_otherInstancePid != null)
            return;
        try
        {
            var config = AppConfig.Load();
            config.WindowWidth = Width;
            config.WindowHeight = Height;
            config.LogHeight = SaneLogHeight(RootGrid.RowDefinitions[LogRowIndex].ActualHeight);
            config.Save();
        }
        catch (Exception ex)
        {
            FileLog.Write($"[gui] could not save the layout: {ex.Message}");
        }
    }

    /// <summary>
    /// A log height the window can actually honour: at least its floor, and never more
    /// than half the window. A fixed row takes its height whether or not there is room,
    /// so a value saved on a taller screen would otherwise push the list off the bottom
    /// rather than being trimmed to fit.
    /// </summary>
    private double SaneLogHeight(double requested)
    {
        var row = RootGrid.RowDefinitions[LogRowIndex];
        var cap = Math.Max(row.MinHeight, Height / 2);
        return Math.Clamp(requested, row.MinHeight, cap);
    }

    private bool _measuringWorkspace;

    /// <summary>
    /// Fill the workspace size in afterwards. Measuring tens of thousands of extracted
    /// files takes a second or two, and this repaint runs after every operation, so the
    /// row shows the path at once and gains the size when it arrives. One at a time:
    /// several refreshes in quick succession should not start several passes over
    /// 50-odd GB.
    /// </summary>
    private void MeasureWorkspaceInBackground()
    {
        if (_measuringWorkspace)
            return;
        _measuringWorkspace = true;
        var root = _workspace.Root;
        _ = Task.Run(() =>
        {
            var size = DiskUsage.Of(root);
            Dispatcher.UIThread.Post(() =>
            {
                _measuringWorkspace = false;
                // The folder may have been moved while this was running.
                if (_workspace.Root != root)
                    return;
                WorkspaceText.Text = size > 0 ? $"{root}   ·   {DiskUsage.Human(size)}" : root;
            });
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
            ChangeWorkspaceButton.IsEnabled = !busy;
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
        bool? MoviesSkipped, bool? HudEnabled, List<Bottle> Bottles,
        Dictionary<string, long> ModSizes);

    /// <summary>
    /// Which parts of the mod loader are still pointed at where the files used to be.
    /// Both paths are written once, at setup, so moving the files afterwards leaves the
    /// game loading from nowhere. Nothing errors when that happens, which is exactly why
    /// it has to be looked for: Panacea going stale means no mods apply at all, and
    /// LuaBackend going stale breaks every Lua mod including the Garden of Assemblage
    /// while the rest still looks fine.
    /// </summary>
    private static List<string> StaleLoaderPaths(AppConfig config, Workspace workspace)
    {
        var stale = new List<string>();
        try
        {
            var bottle = Bottle.Resolve(config);
            if (!PanaceaService.ModPathMatches(config.GameDir!, bottle.ToWindowsPath(workspace.CompiledModRoot)))
                stale.Add("the mod folder");
            if (!LuaBackendService.ScriptsPathMatches(config.GameDir!,
                    LuaBackendService.ExpectedScriptsPath(bottle, workspace)))
                stale.Add("the Lua scripts folder");
        }
        catch
        {
            // Bottle gone or unreadable; the game row already reports that.
        }
        return stale;
    }

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
                loaderRow = StatusRow.Idle("Installed when you click Run Setup.");
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

                // The game is told once, at setup, where the mods are. Move them and it
                // still starts, finds nothing there, and plays as though no mods were
                // installed. Nothing errors, so this is the only place it can be caught.
                if (missing.Count == 0 && StaleLoaderPaths(config, workspace) is { Count: > 0 } stale)
                    loaderRow = StatusRow.Warn(
                        $"Installed, but still looking for {string.Join(" and ", stale)} where " +
                        $"{(stale.Count == 1 ? "it" : "they")} used to be, so mods will not load. " +
                        "Click Run Setup to fix it.");
            }

            var dataRow = !ExtractionService.LooksExtracted(workspace.DataDir)
                ? StatusRow.Idle("Not extracted yet. Click Extract Game Data after setup.")
                : gameOk && ExtractionService.IsExtractionStale(config.GameDir!, config.Language, workspace.DataDir)
                    ? StatusRow.Warn("Out of date after a game update. Run Extract Game Data, then Build.")
                    : StatusRow.Ok("Extracted.");

            var mods = new ModsService(workspace).List();
            // One du call for the whole list; twenty processes to answer one question
            // would cost twenty times as much on every refresh.
            var modSizes = DiskUsage.Of(mods.Select(m => m.Path).ToList());
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
                moviesSkipped, hudEnabled, bottles, modSizes));
        });

        _config = snapshot.config;
        _workspace = snapshot.workspace;
        var status = snapshot.Item3;

        PaintStatusRow(GameStatusBadge, GameStatusIcon, GameStatusText, status.Game);
        PaintStatusRow(LoaderStatusBadge, LoaderStatusIcon, LoaderStatusText, status.Loader);
        PaintStatusRow(DataStatusBadge, DataStatusIcon, DataStatusText, status.Data);
        RefreshBottlePicker(status.Bottles);
        WorkspaceText.Text = _config.WorkspaceRoot;
        MeasureWorkspaceInBackground();
        GamePathText.Text = status.GamePath ?? "";
        GamePathText.IsVisible = status.GamePath != null;
        // Movies on means the game dies at the next cutscene, so the button says so
        // rather than reading as an ordinary toggle someone has left in either position.
        var moviesOn = status.MoviesSkipped == false;
        MoviesButton.Content = status.MoviesSkipped == true ? "Movies: Skipped" : "Movies: On";
        MoviesButton.Classes.Set("warning", moviesOn);
        ToolTip.SetTip(MoviesButton, moviesOn
            ? "Movies are on, and cutscenes crash the game under CrossOver. Click to skip them."
            : "Cutscenes are skipped, which is what stops them crashing the game. Click to put them back.");
        MoviesButton.IsEnabled = status.MoviesSkipped != null && !_busy;
        HudButton.Content = status.HudEnabled == true ? "FPS HUD: On" : "FPS HUD: Off";
        HudButton.IsEnabled = status.HudEnabled != null && !_busy;

        _refreshing = true;
        _mods.Clear();
        foreach (var mod in status.Mods)
        {
            var row = new ModRow
            {
                Name = mod.Name,
                Title = mod.Metadata?.Title ?? mod.Name,
                Enabled = mod.Enabled,
                Issue = KnownIssues.For(mod.Name),
                Size = status.ModSizes.TryGetValue(mod.Path, out var bytes)
                    ? DiskUsage.Human(bytes)
                    : null,
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
        Log("Setup complete. Next: Extract Game Data (one time, a few minutes).");
    });

    /// <summary>
    /// Move where mods, the extracted data and the build are kept. The extraction alone
    /// is about 30 GB, and the Macs this runs on often have less spare than that, so
    /// this has to be changeable rather than fixed at the home folder.
    /// </summary>
    private async void OnChangeWorkspace(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        await RunTask("Move files", () => PickAndMoveWorkspaceAsync());
    }

    /// <summary>
    /// Ask for a folder and move everything there, returning whether it moved. Does the
    /// work inline rather than through RunTask so the first extraction can offer the
    /// same choice from inside its own operation.
    /// </summary>
    private async Task<bool> PickAndMoveWorkspaceAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where to keep KH2 Rando's files",
            AllowMultiple = false,
        });
        var chosen = picked.FirstOrDefault()?.Path.LocalPath;
        if (chosen == null)
            return false;

        // Keep everything inside a named folder rather than scattering it across whatever
        // was picked, unless they picked such a folder already.
        var target = string.Equals(Path.GetFileName(chosen.TrimEnd('/')), "KH2 Rando", StringComparison.Ordinal)
            ? chosen
            : Path.Combine(chosen, "KH2 Rando");
        var current = _config.WorkspaceRoot;
        if (string.Equals(Path.GetFullPath(target), Path.GetFullPath(current), StringComparison.Ordinal))
        {
            Log("That is already where the files are.");
            return false;
        }

        var (size, sameVolume, free) = await Task.Run(() => (
            WorkspaceMover.SizeOnDisk(current),
            WorkspaceMover.SameVolume(current, target),
            WorkspaceMover.FreeSpace(target)));
        var effort = sameVolume
            ? "Both are on the same disk, so moving is a rename and is immediate."
            : $"They are on different disks, so about {size / 1024 / 1024 / 1024} GB has to be copied. " +
              "That takes a while, and the old copy is only removed once the new one is complete.";

        if (!await ConfirmAsync("Move the KH2 Rando files",
                $"Everything moves from:\n{current}\n\nto:\n{target}\n\n{effort}\n\n" +
                $"{free / 1024 / 1024 / 1024} GB free there.", "Move"))
            return false;

        await Task.Run(() =>
        {
            WorkspaceMover.Move(current, target, Log);
            var config = AppConfig.Load();
            config.WorkspaceRoot = target;
            config.Save();
        });
        _config = AppConfig.Load();
        _workspace = new Workspace(_config.WorkspaceRoot);
        Log($"Files now kept in {_workspace.Root}.");

        // The game holds the old location, written into its folder at setup, and would
        // go on loading mods from where they are not. Moving the files without saying so
        // is precisely how someone ends up with a game that runs and ignores every mod.
        await RepointGameAtMovedFilesAsync();
        return true;
    }

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
    /// Record an install and put the mod loader in it. Setup and the game-folder picker are the
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
        // Asked every time, not just the first. This writes 30 GB and runs for twenty
        // minutes, so it is worth a deliberate start, and a re-extraction after a game
        // update is exactly when someone might want it on a different disk.
        const long needed = 32L * 1024 * 1024 * 1024;
        var free = await Task.Run(() => WorkspaceMover.FreeSpace(_workspace.Root));
        var room = free > 0 ? $"{free / 1024 / 1024 / 1024} GB free" : "free space unknown";
        var choice = await ChooseAsync("Where should the game data go?",
            "Extracting unpacks about 30 GB and takes a few minutes. Choose where it is " +
            "kept; it can be moved later, but moving it afterwards means shifting all of it again.",
            new List<string>
            {
                $"{_workspace.Root}\n{room}",
                "Choose another folder\u2026",
            });
        if (choice < 0)
        {
            Log("Extraction cancelled.");
            return;
        }
        if (choice == 1)
        {
            if (!await PickAndMoveWorkspaceAsync())
            {
                Log("Extraction cancelled; the files stay where they were.");
                return;
            }
            free = await Task.Run(() => WorkspaceMover.FreeSpace(_workspace.Root));
        }

        // Refused rather than discovered by filling someone's disk.
        if (free > 0 && free < needed)
        {
            await NoticeAsync("Not enough room",
                $"Extracting needs about 30 GB and there is {free / 1024 / 1024 / 1024} GB free where " +
                $"the files are kept:\n\n{_workspace.Root}\n\n" +
                "Click the folder icon on the Files row to put them on a drive with room, " +
                "then extract again.");
            Log($"Extraction stopped: about 30 GB needed, {free / 1024 / 1024 / 1024} GB free at {_workspace.Root}.");
            return;
        }

        Log("Extracting KH2 data, usually a few minutes. Leave the app open.");
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


    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        // Asked as a choice rather than a yes/no, because there are two quite different
        // things someone might mean by "reset" and one of them throws away hours.
        var dataSize = await Task.Run(() => DiskUsage.Of(_workspace.DataDir));
        var extracted = dataSize > 0 ? $" ({DiskUsage.Human(dataSize)})" : "";
        var choice = await ChooseAsync("Reset",
            "Both remove the mod loader, LuaBackend and the changes made to your bottle, and " +
            "put the movie folder back. Quit Steam and the game first.",
            new List<string>
            {
                "Undo the setup\nMods, seeds, and the extracted game data are kept",
                $"Undo the setup and delete the extracted game data{extracted}\nExtracting again takes a few minutes",
            });
        if (choice < 0)
            return;

        var deleteData = choice == 1;
        var whatGoes = dataSize > 0
            ? $"the extracted game data ({DiskUsage.Human(dataSize)})"
            : "the extracted game data";
        if (deleteData && !await ConfirmAsync("Delete the extracted game data",
                $"This deletes {whatGoes} from:\n\n" + _workspace.DataDir + "\n\n" +
                "Nothing can be built until it has been extracted again, which takes a few " +
                "minutes. Your mods and seeds are not touched.",
                "Delete"))
            return;

        await RunTask("Reset to vanilla", () => Task.Run(() =>
            new SetupService().ResetToVanilla(_config, Log, deleteData)));
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

    private int? _otherInstancePid;
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
            // Keep it with the mods, the extracted data and the tracker, so moving the
            // Files folder takes the generator with it instead of stranding it at home.
            psi.ArgumentList.Add(Path.Combine(_workspace.Root, "seedgen"));
            using var process = System.Diagnostics.Process.Start(psi)!;
            process.OutputDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) Log(args.Data); };
            process.ErrorDataReceived += (_, args) => { if (!string.IsNullOrWhiteSpace(args.Data)) Log(args.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Installer did not finish, see the messages above.");
            Log("Seed Generator installed; it's on your Desktop. Click this button again to open it.");
        }));
    }

    private async void OnInstallGoa(object? sender, RoutedEventArgs e)
    {
        const string goa = "KH2FM-Mods-Num/GoA-ROM-Edition";
        if (await EnableIfAlreadyInstalled(goa, "Garden of Assemblage"))
            return;
        await RunTask("Install Garden of Assemblage", () => Task.Run(() =>
        {
            var mods = new ModsService(_workspace);
            var name = mods.InstallFromGit(goa, Log);
            mods.SetEnabled(name, true);
            Log("Enabled. Click Build to apply.");
        }));
    }

    /// <summary>
    /// The install buttons' already-installed case. Stopping at "already installed"
    /// left the actual intent unmet: someone clicking Install GoA with GoA sitting
    /// unticked in the list wanted it on. Enable it and say so; only when it is
    /// installed and already on is there truly nothing to do.
    /// </summary>
    private async Task<bool> EnableIfAlreadyInstalled(string modName, string title)
    {
        if (!_workspace.IsModInstalled(modName))
            return false;
        var enabled = _workspace.EnabledMods()
            .Contains(modName, StringComparer.OrdinalIgnoreCase);
        if (enabled)
        {
            Log($"{title} is already installed and enabled.");
            return true;
        }
        await RunTask($"Enable {title}", () => Task.Run(() =>
        {
            new ModsService(_workspace).SetEnabled(modName, true);
            Log($"{title} was already installed but switched off; it is enabled again. Click Build to apply.");
        }));
        return true;
    }

    private async void OnInstallRefined(object? sender, RoutedEventArgs e)
    {
        if (await EnableIfAlreadyInstalled(RefinedService.MainMod, "Re:Fined"))
            return;
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
            Log("Enabled. Click Build to apply. If the bottle is missing the .NET runtime");
            Log("Re:Fined runs on, Build offers to install it first, one time.");
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

        // Build succeeds whether or not the game can find what it builds, so this is
        // worth repeating here: nothing else would tell them before they played.
        if (_config.GameDir != null && GameLocator.IsGameDir(_config.GameDir)
            && StaleLoaderPaths(_config, _workspace) is { Count: > 0 } stalePaths)
            Log($"WARNING: the game is still looking for {string.Join(" and ", stalePaths)} where " +
                $"{(stalePaths.Count == 1 ? "it" : "they")} used to be, so what was just built will " +
                "not load. Click Run Setup, then Build again.");

        // Reset restores the movie folder, so a bottle that was reset and set up again
        // is playing cutscenes once more, and cutscenes crash the game here. Said at
        // build time because that is the last moment before someone plays.
        if (_config.GameDir != null && GameLocator.IsGameDir(_config.GameDir)
            && !MovieService.AreMoviesSkipped(_config.GameDir))
            Log("WARNING: Movies are on. Cutscenes crash the game under CrossOver, so the game " +
                "will run until it reaches one. Click the Movies button to skip them.");
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
