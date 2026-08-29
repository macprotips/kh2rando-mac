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

/// <summary>
/// Updating means replacing the .app while the settings and workspace stay where they
/// are, so a new build has to read what an older one wrote, and an older build has to
/// survive what a newer one wrote.
/// </summary>
public class UpgradeCompatibilityTests : IDisposable
{
    private readonly string _dir;

    public UpgradeCompatibilityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ReadsAConfigFromAnOlderBuildThatLacksNewerFields()
    {
        // The shape 0.1.0 wrote: no CrossOverAppPath, no NoticeShown, no Language.
        var path = Write("""
            {
              "BottleName": "KH2",
              "GameDir": "/Volumes/MPT/game",
              "Launcher": "Steam",
              "WorkspaceRoot": "/Users/someone/KH2 Rando"
            }
            """);

        var config = AppConfig.Load(path);
        Assert.Equal("KH2", config.BottleName);
        Assert.Equal("/Volumes/MPT/game", config.GameDir);
        Assert.Equal("Steam", config.Launcher);
        Assert.Null(config.CrossOverAppPath);
        Assert.False(config.NoticeShown);
    }

    [Fact]
    public void IgnoresFieldsAFutureBuildMightAdd()
    {
        // Someone running an older build after a newer one must not lose their setup.
        var path = Write("""
            {
              "BottleName": "KH2",
              "GameDir": "/Volumes/MPT/game",
              "Launcher": "Steam",
              "SomethingAddedLater": {"nested": true}
            }
            """);

        var config = AppConfig.Load(path);
        Assert.Equal("KH2", config.BottleName);
        Assert.Equal("/Volumes/MPT/game", config.GameDir);
    }

    [Fact]
    public void SettingsSurviveASaveAndReload()
    {
        var path = Path.Combine(_dir, "config.json");
        new AppConfig
        {
            BottleName = "KH2",
            GameDir = "/Volumes/MPT/game",
            Launcher = "EGS",
            NoticeShown = true,
            CrossOverAppPath = "/Applications/CrossOver.app",
        }.Save(path);

        var reloaded = AppConfig.Load(path);
        Assert.Equal("KH2", reloaded.BottleName);
        Assert.Equal("EGS", reloaded.Launcher);
        Assert.True(reloaded.NoticeShown);
        Assert.Equal("/Applications/CrossOver.app", reloaded.CrossOverAppPath);
    }
}
