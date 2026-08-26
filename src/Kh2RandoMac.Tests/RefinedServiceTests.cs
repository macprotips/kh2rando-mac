using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class RefinedServiceTests
{
    [Theory]
    [InlineData("KH-ReFined/KH2-MAIN", true)]
    [InlineData("KH-ReFined/KH2-JapaneseVO", true)]
    [InlineData("kh-refined/KH2-MAIN", true)]
    [InlineData("KH2FM-Mods-Num/GoA-ROM-Edition", false)]
    [InlineData("randoseed1", false)]
    public void IsRefinedMod_MatchesTheOrgPrefix(string name, bool expected)
    {
        Assert.Equal(expected, RefinedService.IsRefinedMod(name));
    }

    [Fact]
    public void ConflictingEnabledMods_FlagsNonRefinedModsOnlyWhenRefinedEnabled()
    {
        var root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        var workspace = new Workspace(root);
        workspace.EnsureDirectories();
        try
        {
            workspace.SaveEnabledMods(new[] { "randoseed1", "KH2FM-Mods-Num/GoA-ROM-Edition" });
            Assert.Empty(RefinedService.ConflictingEnabledMods(workspace));

            workspace.SaveEnabledMods(new[]
                { "KH-ReFined/KH2-MAIN", "randoseed1", "KH2FM-Mods-Num/GoA-ROM-Edition" });
            Assert.Equal(new[] { "randoseed1", "KH2FM-Mods-Num/GoA-ROM-Edition" },
                RefinedService.ConflictingEnabledMods(workspace));

            workspace.SaveEnabledMods(new[] { "KH-ReFined/KH2-MAIN", "KH-ReFined/KH2-JapaneseVO" });
            Assert.Empty(RefinedService.ConflictingEnabledMods(workspace));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void HasDesktopRuntime_RequiresAnInstalled8xFramework()
    {
        using var fake = new FakeBottle();
        Assert.False(RefinedService.HasDesktopRuntime(fake.Bottle));

        var shared = Path.Combine(fake.Bottle.DriveC, "Program Files", "dotnet",
            "shared", "Microsoft.WindowsDesktop.App");
        Directory.CreateDirectory(Path.Combine(shared, "6.0.1"));
        Assert.False(RefinedService.HasDesktopRuntime(fake.Bottle));

        Directory.CreateDirectory(Path.Combine(shared, "8.0.11"));
        Assert.True(RefinedService.HasDesktopRuntime(fake.Bottle));
    }
}
