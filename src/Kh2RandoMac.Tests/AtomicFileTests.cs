using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir;

    public AtomicFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void WriteAllLines_ReplacesTheFileAndLeavesNoStagingBehind()
    {
        var path = Path.Combine(_dir, "user.reg");
        File.WriteAllText(path, "old contents");

        AtomicFile.WriteAllLines(path, new[] { "WINE REGISTRY Version 2", "\"version\"=\"native,builtin\"" });

        Assert.Equal(new[] { "WINE REGISTRY Version 2", "\"version\"=\"native,builtin\"" },
            File.ReadAllLines(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Write_LeavesTheOriginalUntouchedWhenWritingFails()
    {
        var path = Path.Combine(_dir, "user.reg");
        File.WriteAllText(path, "the only good copy");

        // An enumerable that throws part way is the shape of a disk filling up.
        IEnumerable<string> Failing()
        {
            yield return "first line";
            throw new IOException("no space left on device");
        }

        Assert.Throws<IOException>(() => AtomicFile.WriteAllLines(path, Failing()));
        Assert.Equal("the only good copy", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Write_ConcurrentWritersNeverLeaveAHalfWrittenFile()
    {
        var path = Path.Combine(_dir, "user.reg");
        Parallel.For(0, 40, i => AtomicFile.WriteAllLines(path, new[] { $"line-{i}", "tail" }));

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("line-", lines[0]);
        Assert.Equal("tail", lines[1]);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void WriteAllText_CreatesAMissingDirectory()
    {
        var path = Path.Combine(_dir, "nested", "deeper", "config.json");
        AtomicFile.WriteAllText(path, "{}");
        Assert.Equal("{}", File.ReadAllText(path));
    }
}
