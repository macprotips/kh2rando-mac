using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class KnownIssuesTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public KnownIssuesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        _workspace = new Workspace(_root);
        _workspace.EnsureDirectories();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Theory]
    [InlineData("thenja09/mastertreasuremagnet")]
    [InlineData("THENJA09/MasterTreasureMagnet")]
    public void For_FlagsConfirmedBadMods(string mod)
    {
        var note = KnownIssues.For(mod);
        Assert.NotNull(note);
        Assert.Contains("freeze", note, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("KH2FM-Mods-Num/GoA-ROM-Edition")]
    [InlineData("SapphireSapphic/FormMovementPlusPlus")]
    [InlineData("randoseed")]
    [InlineData(null)]
    public void For_LeavesEverythingElseAlone(string? mod)
    {
        Assert.Null(KnownIssues.For(mod));
    }

    [Fact]
    public void ForEnabled_ReportsOnlyEnabledOffenders()
    {
        _workspace.SaveEnabledMods(new[] { "KH2FM-Mods-Num/GoA-ROM-Edition", "thenja09/mastertreasuremagnet" });
        var notes = KnownIssues.ForEnabled(_workspace);
        Assert.Single(notes);
        Assert.Contains("Master Treasure Magnet", notes[0]);

        _workspace.SaveEnabledMods(new[] { "KH2FM-Mods-Num/GoA-ROM-Edition" });
        Assert.Empty(KnownIssues.ForEnabled(_workspace));
    }
}
