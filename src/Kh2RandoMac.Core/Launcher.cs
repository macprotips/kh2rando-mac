using System.Diagnostics;

namespace Kh2RandoMac.Core;

public static class Launcher
{
    public const string SteamAppId = "2552430"; // KINGDOM HEARTS -HD 1.5+2.5 ReMIX- (Steam)

    /// <summary>
    /// Launch the game, going straight into KH2 rather than stopping at the
    /// collection's game-select menu. Mods apply either way, since Panacea is enabled
    /// by bottle-wide registry DLL overrides rather than anything set here, so
    /// starting the game from CrossOver by hand still works; it just shows the menu.
    /// </summary>
    /// <returns>A user-facing note about how the game was launched.</returns>
    public static string LaunchKh2(AppConfig config)
    {
        var bottle = Bottle.Resolve(config);

        if (bottle.Platform == WinePlatform.Sikarugir)
        {
            // A wrapper launches whatever program it was built around (usually Steam).
            Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { bottle.WrapperApp! } });
            return "Wrapper launched. Start the game from Steam inside it.";
        }

        var gameDir = config.GameDir
            ?? throw new InvalidOperationException("No game folder set. Click Run Setup first.");

        // Steam only starts the collection launcher, never a game inside it, so the
        // menu is unavoidable on the way in. Panacea is loaded into the launcher as
        // well as the games (one version.dll beside all of them), and quick_launch
        // makes it call the launcher's own "start this game" routine straight away.
        // Going around the launcher and starting KH2's exe ourselves is the obvious
        // approach and the one that does not work.
        //
        // Panacea consumes the setting and rewrites the file without it, so that a
        // launch nobody asked us about still shows the menu. That means writing it
        // before every launch rather than once at install.
        string target;
        if (config.Launcher == "Steam")
        {
            if (PanaceaService.IsInstalled(gameDir))
                PanaceaService.SetSetting(gameDir, "quick_launch", PanaceaService.Kh2LaunchCode);
            target = $"steam://rungameid/{SteamAppId}";
        }
        else
        {
            // The Epic copy can be started directly, so the launcher never appears.
            target = Path.Combine(gameDir, GameLocator.Kh2ExeName);
        }

        var psi = new ProcessStartInfo(CrossOverApp.BinIn("cxstart", bottle.OwningApp)) { UseShellExecute = false };
        psi.ArgumentList.Add("--bottle");
        psi.ArgumentList.Add(bottle.Name);
        psi.ArgumentList.Add(target);
        Process.Start(psi);

        if (config.Launcher != "Steam")
            return "Launching KH2 via CrossOver.";
        return PanaceaService.IsInstalled(gameDir)
            ? "Launch requested via Steam. The launcher opens and goes straight into KH2."
            : "Launch requested via Steam. Install the mod loader to skip the launcher menu.";
    }
}
