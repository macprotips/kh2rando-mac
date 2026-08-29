using Avalonia;

namespace Kh2RandoMac.Gui;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

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
