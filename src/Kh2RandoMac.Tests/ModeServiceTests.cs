using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class ModeServiceTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public ModeServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        _workspace = new Workspace(_root);
        _workspace.EnsureDirectories();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private void InstallFakeMod(string name)
    {
        var dir = Path.Combine(_workspace.ModsDir, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mod.yml"), "title: fake");
    }

    [Fact]
    public void Switch_ParksAndRestoresEachModesList()
    {
        var config = new AppConfig();
        InstallFakeMod("KH-ReFined/KH2-MAIN");
        _workspace.SaveEnabledMods(new[] { "randoseed1", "KH2FM-Mods-Num/GoA-ROM-Edition" });

        // Rando → Re:Fined: first switch defaults to the installed Re:Fined mods.
        Assert.Equal(ModeService.Refined, ModeService.Switch(config, _workspace));
        Assert.Equal(ModeService.Refined, config.ActiveMode);
        Assert.Equal(new[] { "KH-ReFined/KH2-MAIN" }, _workspace.EnabledMods());

        // Changes made in Re:Fined mode survive the round trip.
        _workspace.SaveEnabledMods(new[] { "KH-ReFined/KH2-MAIN", "KH-ReFined/KH2-JapaneseVO" });

        // Re:Fined → rando: the parked rando list comes back untouched.
        Assert.Equal(ModeService.Rando, ModeService.Switch(config, _workspace));
        Assert.Equal(new[] { "randoseed1", "KH2FM-Mods-Num/GoA-ROM-Edition" }, _workspace.EnabledMods());

        // Rando → Re:Fined again: the modified Re:Fined list was parked, not reset.
        ModeService.Switch(config, _workspace);
        Assert.Equal(new[] { "KH-ReFined/KH2-MAIN", "KH-ReFined/KH2-JapaneseVO" }, _workspace.EnabledMods());
    }

    [Theory]
    [InlineData(null, ModeService.Rando)]
    [InlineData("rando", ModeService.Rando)]
    [InlineData("REFINED", ModeService.Refined)]
    [InlineData("garbage", ModeService.Rando)]
    public void Normalize_IsForgiving(string? input, string expected)
    {
        Assert.Equal(expected, ModeService.Normalize(input));
    }
}
