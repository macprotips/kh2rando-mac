using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace Kh2RandoMac.Gui;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            // Files dropped on the dock icon (or opened via Finder) arrive as
            // activation events on macOS.
            if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs fileArgs)
                    {
                        var paths = fileArgs.Files
                            .Select(f => f.TryGetLocalPath())
                            .Where(p => p != null)
                            .Select(p => p!)
                            .ToList();
                        if (paths.Count > 0)
                            _ = mainWindow.InstallFilesAsync(paths);
                    }
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
