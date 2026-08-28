using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class PanaceaSettingsTests : IDisposable
{
    private readonly string _gameDir;

    public PanaceaSettingsTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_gameDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, true); } catch { }
    }

    private string SettingsPath => Path.Combine(_gameDir, PanaceaService.SettingsFileName);

    [Fact]
    public void SetSetting_CreatesTheFileWhenPanaceaHasNotWrittenOneYet()
    {
        PanaceaService.SetSetting(_gameDir, "quick_launch", PanaceaService.Kh2LaunchCode);
        Assert.Equal(new[] { "quick_launch=kh2" }, File.ReadAllLines(SettingsPath));
    }

    [Fact]
    public void SetSetting_KeepsTheOtherSettingsIntact()
    {
        File.WriteAllLines(SettingsPath, new[] { "mod_path=C:\\mods", "show_console=false" });
        PanaceaService.SetSetting(_gameDir, "quick_launch", PanaceaService.Kh2LaunchCode);
        Assert.Equal(
            new[] { "mod_path=C:\\mods", "show_console=false", "quick_launch=kh2" },
            File.ReadAllLines(SettingsPath));
    }

    [Fact]
    public void SetSetting_ReplacesAnExistingValueRatherThanAddingASecondOne()
    {
        File.WriteAllLines(SettingsPath, new[] { "quick_launch=kh1", "mod_path=C:\\mods" });
        PanaceaService.SetSetting(_gameDir, "quick_launch", PanaceaService.Kh2LaunchCode);
        Assert.Equal(new[] { "mod_path=C:\\mods", "quick_launch=kh2" }, File.ReadAllLines(SettingsPath));
    }

    [Fact]
    public void SetSetting_CollapsesTheDuplicatesModsManagerAppendsEveryRun()
    {
        File.WriteAllLines(SettingsPath, new[]
        {
            "mod_path=C:\\mods",
            "quick_launch=kh2",
            "quick_launch=kh2",
            "quick_launch=kh2",
        });
        PanaceaService.SetSetting(_gameDir, "quick_launch", PanaceaService.Kh2LaunchCode);
        Assert.Equal(new[] { "mod_path=C:\\mods", "quick_launch=kh2" }, File.ReadAllLines(SettingsPath));
    }

    [Fact]
    public void SetSetting_DoesNotMistakeALongerKeyForTheOneBeingSet()
    {
        File.WriteAllLines(SettingsPath, new[] { "quick_launch_delay=5" });
        PanaceaService.SetSetting(_gameDir, "quick_launch", PanaceaService.Kh2LaunchCode);
        Assert.Equal(new[] { "quick_launch_delay=5", "quick_launch=kh2" }, File.ReadAllLines(SettingsPath));
    }
}
