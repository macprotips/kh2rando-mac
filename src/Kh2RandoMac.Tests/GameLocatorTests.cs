using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class GameLocatorTests : IDisposable
{
    private readonly string _root;

    public GameLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Theory]
    [InlineData("/Volumes/Games/SteamLibrary/steamapps/common/KINGDOM HEARTS -HD 1.5+2.5 ReMIX-")]
    [InlineData("/Users/someone/Library/Application Support/CrossOver/Bottles/KH2/drive_c/Program Files (x86)/Steam/steamapps/common/KH")]
    public void InferLauncher_ReadsSteamOffTheLibraryLayout(string dir)
    {
        Assert.Equal("Steam", GameLocator.InferLauncher(dir, "EGS"));
    }

    [Theory]
    [InlineData("/Volumes/Games/KH_1.5_2.5")]
    [InlineData("/Volumes/Games/KH_1.5_2.5/")]
    [InlineData("/Volumes/Games/Program Files/Epic Games/kh_1.5_2.5")]
    public void InferLauncher_ReadsEpicOffTheInstallerFolderName(string dir)
    {
        Assert.Equal("EGS", GameLocator.InferLauncher(dir, "Steam"));
    }

    [Fact]
    public void InferLauncher_KeepsWhatTheUserAlreadyHasWhenTheLayoutSaysNothing()
    {
        Assert.Equal("EGS", GameLocator.InferLauncher("/Volumes/Games/KH copy", "EGS"));
        Assert.Equal("Steam", GameLocator.InferLauncher("/Volumes/Games/KH copy", "Steam"));
    }

    [Fact]
    public void ForFolder_RefusesAFolderWithNoGameInIt()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GameLocator.ForFolder(_root, null, "Steam"));
        Assert.Contains(GameLocator.Kh2ExeName, ex.Message);
    }

    [Fact]
    public void IsGameDir_WantsTheExeNotJustTheFolder()
    {
        Assert.False(GameLocator.IsGameDir(_root));
        File.WriteAllText(Path.Combine(_root, GameLocator.Kh2ExeName), "");
        Assert.True(GameLocator.IsGameDir(_root));
    }

    [Fact]
    public void ParsesLibraryFoldersVdf()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                }
                "1"
                {
                    "path"		"L:\\SteamLibrary"
                }
            }
            """;
        var paths = GameLocator.ParseLibraryFolders(vdf);
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"L:\SteamLibrary" }, paths);
    }

    [Fact]
    public void EmptyVdfYieldsNothing()
    {
        Assert.Empty(GameLocator.ParseLibraryFolders("\"libraryfolders\"\n{\n}\n"));
    }
}

public class AppConfigTests : IDisposable
{
    private readonly string _dir;

    public AppConfigTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string ConfigPath => Path.Combine(_dir, "config.json");

    [Fact]
    public void Save_RoundTripsAndLeavesNoStagingFileBehind()
    {
        new AppConfig { BottleName = "KH2", GameDir = "/games/kh", Launcher = "EGS" }.Save(ConfigPath);

        var loaded = AppConfig.Load(ConfigPath);
        Assert.Equal("KH2", loaded.BottleName);
        Assert.Equal("/games/kh", loaded.GameDir);
        Assert.Equal("EGS", loaded.Launcher);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Save_ConcurrentWritersNeverLeaveTheConfigUnreadable()
    {
        // Two copies of the app saving at once used to share one staging file, so one
        // could truncate the other's text and then move the wreckage into place.
        Parallel.For(0, 40, i =>
            new AppConfig { BottleName = $"bottle{i}", GameDir = "/games/kh" }.Save(ConfigPath));

        var loaded = AppConfig.Load(ConfigPath);
        Assert.StartsWith("bottle", loaded.BottleName);
        Assert.Equal("/games/kh", loaded.GameDir);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
}
