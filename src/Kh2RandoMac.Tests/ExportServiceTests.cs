using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class ExportServiceTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public ExportServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        _workspace = new Workspace(Path.Combine(_root, "workspace"));
        _workspace.EnsureDirectories();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private void InstallFakeMod(string name, string fileName = "mod.yml")
    {
        var dir = Path.Combine(_workspace.ModsDir, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.Combine(dir, "nested"));
        File.WriteAllText(Path.Combine(dir, "mod.yml"), "title: fake");
        File.WriteAllText(Path.Combine(dir, "nested", fileName), "payload");
    }

    [Fact]
    public void Export_CopiesModsNestedFilesAndLoadOrder()
    {
        InstallFakeMod("author/one");
        InstallFakeMod("two");
        _workspace.SaveEnabledMods(new[] { "two", "author/one" });
        var dest = Path.Combine(_root, "out");
        Directory.CreateDirectory(dest);

        var made = ExportService.Export(_workspace, dest);

        // Everything lands inside one named folder, not loose in the chosen one.
        Assert.Equal(ExportService.FolderName, Path.GetFileName(made));
        Assert.Equal(new[] { made }, Directory.GetDirectories(dest));
        Assert.True(File.Exists(Path.Combine(made, "mods", "author", "one", "mod.yml")));
        Assert.True(File.Exists(Path.Combine(made, "mods", "author", "one", "nested", "mod.yml")));
        Assert.True(File.Exists(Path.Combine(made, "mods", "two", "mod.yml")));
        Assert.True(File.Exists(Path.Combine(made, ExportService.ReadmeName)));
        // Load order is what decides conflicts, so it has to travel with the files.
        Assert.Equal(new[] { "two", "author/one" },
            File.ReadAllLines(Path.Combine(made, ExportService.OrderFileName)));
    }

    [Fact]
    public void Export_NeverOverwritesAnEarlierExport()
    {
        InstallFakeMod("one");
        var dest = Path.Combine(_root, "out");
        Directory.CreateDirectory(dest);

        var first = ExportService.Export(_workspace, dest);
        var second = ExportService.Export(_workspace, dest);

        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first));
        Assert.EndsWith("2", Path.GetFileName(second));
    }

    [Fact]
    public void Export_RefusesWhenNothingIsInstalled()
    {
        var dest = Path.Combine(_root, "out");
        Directory.CreateDirectory(dest);
        Assert.Throws<InvalidOperationException>(() => ExportService.Export(_workspace, dest));
    }

    [Theory]
    [InlineData(500L, "KB")]
    [InlineData(5L * 1024 * 1024, "MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "GB")]
    public void DescribeSize_PicksSensibleUnits(long bytes, string unit)
    {
        Assert.EndsWith(unit, ExportService.DescribeSize(bytes));
    }
}
