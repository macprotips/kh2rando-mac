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

        var count = ExportService.Export(_workspace, dest);

        Assert.Equal(2, count);
        Assert.True(File.Exists(Path.Combine(dest, "mods", "author", "one", "mod.yml")));
        Assert.True(File.Exists(Path.Combine(dest, "mods", "author", "one", "nested", "mod.yml")));
        Assert.True(File.Exists(Path.Combine(dest, "mods", "two", "mod.yml")));
        Assert.True(File.Exists(Path.Combine(dest, ExportService.ReadmeName)));
        // Load order is what decides conflicts, so it has to travel with the files.
        Assert.Equal(new[] { "two", "author/one" },
            File.ReadAllLines(Path.Combine(dest, ExportService.OrderFileName)));
    }

    [Fact]
    public void Export_RefusesToOverwriteAnExistingExport()
    {
        InstallFakeMod("one");
        var dest = Path.Combine(_root, "out");
        Directory.CreateDirectory(dest);
        ExportService.Export(_workspace, dest);

        var ex = Assert.Throws<InvalidOperationException>(() => ExportService.Export(_workspace, dest));
        Assert.Contains("already contains an export", ex.Message);
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
