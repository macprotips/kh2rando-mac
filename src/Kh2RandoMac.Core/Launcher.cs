using System.Diagnostics;

namespace Kh2RandoMac.Core;

public static class Launcher
{
    public const string SteamAppId = "2552430"; // KINGDOM HEARTS -HD 1.5+2.5 ReMIX- (Steam)

    /// <summary>
    /// Launch the game. Because Panacea is enabled via bottle-wide registry DLL
    /// overrides (not env vars), launching from CrossOver or the wrapper directly
    /// works identically; this is just a convenience.
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
            ?? throw new InvalidOperationException("Game folder not configured.");

        // Launch KH2's exe directly, skipping the collection launcher. The Steam
        // version needs steam_appid.txt next to the exes for a direct launch to
        // work (Steam itself must be running in the bottle).
        if (config.Launcher == "Steam")
        {
            var appIdFile = Path.Combine(gameDir, "steam_appid.txt");
            if (!File.Exists(appIdFile))
                File.WriteAllText(appIdFile, SteamAppId);
        }

        var psi = new ProcessStartInfo(CrossOverApp.CxStart) { UseShellExecute = false };
        psi.ArgumentList.Add("--bottle");
        psi.ArgumentList.Add(bottle.Name);
        psi.ArgumentList.Add(Path.Combine(gameDir, GameLocator.Kh2ExeName));
        Process.Start(psi);
        return config.Launcher == "Steam"
            ? "Launching KH2 directly. Keep Steam running in the bottle."
            : "Launching KH2 via CrossOver.";
    }
}
