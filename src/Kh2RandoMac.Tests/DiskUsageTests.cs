using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class DiskUsageTests : IDisposable
{
    private readonly string _root;

    public DiskUsageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string MakeFolder(string name, int kilobytes)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "payload.bin"), new byte[kilobytes * 1024]);
        return dir;
    }

    [Fact]
    public void Of_MeasuresSeveralFoldersInOneGo()
    {
        var small = MakeFolder("small", 16);
        var larger = MakeFolder("larger", 512);

        var sizes = DiskUsage.Of(new[] { small, larger });

        Assert.True(sizes[small] >= 16 * 1024, $"small was {sizes[small]}");
        Assert.True(sizes[larger] >= 512 * 1024, $"larger was {sizes[larger]}");
        Assert.True(sizes[larger] > sizes[small]);
    }

    [Fact]
    public void Of_HandlesAFolderNameWithSpaces()
    {
        // Every real path here has one: "KH2 Rando", "KINGDOM HEARTS -HD 1.5+2.5 ReMIX-".
        var spaced = MakeFolder("a folder with spaces", 32);
        var sizes = DiskUsage.Of(new[] { spaced });
        Assert.True(sizes[spaced] >= 32 * 1024);
    }

    [Fact]
    public void Of_SkipsFoldersThatAreNotThereRatherThanFailing()
    {
        var real = MakeFolder("real", 16);
        var sizes = DiskUsage.Of(new[] { real, Path.Combine(_root, "missing") });
        Assert.True(sizes.ContainsKey(real));
        Assert.False(sizes.ContainsKey(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void Of_ReturnsNothingWhenAskedAboutNothing()
    {
        Assert.Empty(DiskUsage.Of(Array.Empty<string>()));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(-5, "")]
    [InlineData(4096, "4 KB")]
    [InlineData(1536 * 1024, "1.5 MB")]
    [InlineData(52L * 1024 * 1024, "52 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
    [InlineData(57L * 1024 * 1024 * 1024, "57 GB")]
    public void Human_ReadsAtAGlance(long bytes, string expected)
    {
        Assert.Equal(expected, DiskUsage.Human(bytes));
    }
}
