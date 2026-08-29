using Avalonia;

namespace Kh2RandoMac.Gui;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // A fatal exception otherwise dies without a trace: the OS crash report cannot
        // see into managed frames, so what actually threw is unknowable afterwards. One
        // crash in the field has already gone undiagnosed for exactly this reason.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Kh2RandoMac.Core.FileLog.Write($"[crash] unhandled: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Kh2RandoMac.Core.FileLog.Write($"[crash] unobserved task: {e.Exception}");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Draw on the CPU rather than through Avalonia's OpenGL path. That path
            // crashed here with a null dereference tearing down a rendering session
            // (AvnGlRenderingSession) moments after Build & Run handed the screen to the
            // game, which is the one thing this app exists to do. Nothing on screen is
            // more than text, a list and a progress bar, so the GPU buys nothing worth
            // that risk, least of all while CrossOver is taking over the display.
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = new[] { AvaloniaNativeRenderingMode.Software },
            })
            .WithInterFont()
            .LogToTrace();
}
