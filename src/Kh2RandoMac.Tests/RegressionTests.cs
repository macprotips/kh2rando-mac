using System.IO.Compression;
using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

/// <summary>Tests pinning the fixes from the first full code review.</summary>
public class RegressionTests
{
    [Fact]
    public void ModPath_RejectsDot_TheWipeEverythingCase()
    {
        using var temp = new TempWorkspace();
        // A zip named "..zip" yields mod name "." → must never resolve to the mods dir itself.
        Assert.Throws<ArgumentException>(() => temp.Workspace.ModPath("."));
        Assert.Throws<ArgumentException>(() => temp.Workspace.ModPath("./"));
    }

    [Fact]
    public void InstallFromZip_RefusesToReplaceAuthorFolder()
    {
        using var temp = new TempWorkspace();
        // Simulate a git install: mods/kh2/SomeAuthor/SomeMod.
        var gitMod = Path.Combine(temp.Workspace.ModsDir, "SomeAuthor", "SomeMod");
        Directory.CreateDirectory(gitMod);
        File.WriteAllText(Path.Combine(gitMod, "mod.yml"), "title: X\nassets: []\n");

        // A zip named SomeAuthor.zip collides with the author folder, must refuse, not delete.
        var zipPath = Path.Combine(temp.Root, "SomeAuthor.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("mod.yml");
            using var w = new StreamWriter(entry.Open());
            w.Write("title: collide\nassets: []\n");
        }
        Assert.Throws<InvalidOperationException>(() => new ModsService(temp.Workspace).InstallFromZip(zipPath));
        Assert.True(File.Exists(Path.Combine(gitMod, "mod.yml")), "git-installed mod must survive");
    }

    [Fact]
    public void InstallFromGit_ReturnsNormalizedName()
    {
        using var temp = new TempWorkspace();
        // Trailing slash must be normalized before any name-based lookups.
        var ex = Record.Exception(() => new ModsService(temp.Workspace).InstallFromGit("bad name/with spaces/"));
        Assert.IsType<ArgumentException>(ex);
    }

    [Fact]
    public void InstallFromKh2PcPatch_GeneratesModYml()
    {
        using var temp = new TempWorkspace();
        var patchPath = Path.Combine(temp.Root, "TexturePack.kh2pcpatch");
        using (var zip = ZipFile.Open(patchPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("kh2_first/original/itempic/item-001.imd");
            using var w = new StreamWriter(entry.Open());
            w.Write("fake-image-data");
        }

        var mods = new ModsService(temp.Workspace);
        var name = mods.InstallFromZip(patchPath);
        Assert.Equal("TexturePack", name);

        var modDir = temp.Workspace.ModPath(name);
        Assert.True(File.Exists(Path.Combine(modDir, "mod.yml")));
        Assert.True(File.Exists(Path.Combine(modDir, "itempic", "item-001.imd")));

        var list = mods.List();
        mods.SetEnabled(name, true);
        var mod = mods.List().Single(m => m.Name == name);
        var asset = Assert.Single(mod.Metadata!.Assets);
        Assert.Equal("itempic/item-001.imd", asset.Name);
        Assert.Equal("kh2_first", asset.Package);
        Assert.Equal("copy", asset.Method);
    }

    [Fact]
    public void InstallFromLua_WrapsScriptAsMod()
    {
        using var temp = new TempWorkspace();
        var luaPath = Path.Combine(temp.Root, "AutoSave.lua");
        File.WriteAllLines(luaPath, new[]
        {
            "LUAGUI_NAME = \"Auto Save\"",
            "LUAGUI_AUTH = \"Someone\"",
            "LUAGUI_DESC = \"Saves automatically\"",
            "function _OnFrame() end",
        });

        var mods = new ModsService(temp.Workspace);
        var name = mods.InstallFromLua(luaPath);
        Assert.Equal("AutoSave", name);

        mods.SetEnabled(name, true);
        var mod = mods.List().Single(m => m.Name == name);
        Assert.Equal("Auto Save", mod.Metadata!.Title);
        var asset = Assert.Single(mod.Metadata.Assets);
        Assert.Equal("scripts/AutoSave.lua", asset.Name);
        Assert.True(File.Exists(Path.Combine(temp.Workspace.ModPath(name), "AutoSave.lua")));
    }

    [Fact]
    public void List_SurvivesCorruptModYml()
    {
        using var temp = new TempWorkspace();
        var modDir = Path.Combine(temp.Workspace.ModsDir, "broken-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "mod.yml"), ": : : not yaml [[[");

        var list = new ModsService(temp.Workspace).List();
        var mod = Assert.Single(list);
        Assert.Equal("broken-mod", mod.Name);
        // Metadata may be null or an error placeholder, the list must render either way.
    }

    [Fact]
    public void Build_WithNoModsProducesEmptyVanillaBuild()
    {
        using var temp = new TempWorkspace();
        // Seed the compiled dir with a stale file from a previous build.
        Directory.CreateDirectory(temp.Workspace.CompiledModDir);
        File.WriteAllText(Path.Combine(temp.Workspace.CompiledModDir, "stale.bin"), "old");

        new PatchBuilder(temp.Workspace).Build();

        Assert.False(File.Exists(Path.Combine(temp.Workspace.CompiledModDir, "stale.bin")));
        Assert.True(File.Exists(Path.Combine(temp.Workspace.CompiledModDir, "patch-package-map.txt")));
    }

    [Fact]
    public void Build_FailsBeforeWipingWhenModYmlUnreadable()
    {
        using var temp = new TempWorkspace();
        var modDir = Path.Combine(temp.Workspace.ModsDir, "broken-mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "mod.yml"), ": : : not yaml [[[");
        new ModsService(temp.Workspace).SetEnabled("broken-mod", true);

        Directory.CreateDirectory(temp.Workspace.CompiledModDir);
        File.WriteAllText(Path.Combine(temp.Workspace.CompiledModDir, "previous-build.bin"), "keep me");

        Assert.ThrowsAny<InvalidOperationException>(() => new PatchBuilder(temp.Workspace).Build());
        Assert.True(File.Exists(Path.Combine(temp.Workspace.CompiledModDir, "previous-build.bin")),
            "a failed validation must not destroy the previous build");
    }

    [Fact]
    public void PatchToml_SteamThrowsWhenNeedlesMissing_InsteadOfSilentNoOp()
    {
        const string weird = "[kh2]\nscripts = [{ path = \"x\", relative = true }]\nexe = \"KINGDOM HEARTS II FINAL MIX.exe\"\n";
        Assert.Throws<InvalidOperationException>(() =>
            LuaBackendService.PatchToml(weird, "Y:/mods/kh2/scripts", steam: true));
    }

    [Fact]
    public void AppConfig_RoundTripsAndNormalizesLauncher()
    {
        var path = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"), "config.json");
        var config = new AppConfig { BottleName = "Steam-2", GameDir = "/tmp/game", Launcher = "steam" };
        Assert.Equal("Steam", config.Launcher);
        config.Launcher = "epic";
        Assert.Equal("EGS", config.Launcher);
        config.Save(path);

        var loaded = AppConfig.Load(path);
        Assert.Equal("Steam-2", loaded.BottleName);
        Assert.Equal("EGS", loaded.Launcher);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    [Fact]
    public void AppConfig_RecoversFromCorruptFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"), "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ truncated garbage");

        var loaded = AppConfig.Load(path);
        Assert.Null(loaded.BottleName); // defaults, not a crash
        Assert.True(File.Exists(path + ".corrupt"), "corrupt file should be set aside for diagnosis");
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }
}

public class ExtractionFingerprintTests
{
    [Fact]
    public void DetectsGameUpdateAfterExtraction()
    {
        var root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(root, "game");
        var dataDir = Path.Combine(root, "data");
        var imageDir = Path.Combine(gameDir, "Image", "dt");
        Directory.CreateDirectory(imageDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "kh2"));
        foreach (var pkg in new[] { "first", "second", "third", "fourth", "fifth", "sixth" })
            File.WriteAllText(Path.Combine(imageDir, $"kh2_{pkg}.hed"), "original-content");

        ExtractionService.WriteFingerprint(gameDir, "dt", dataDir);
        Assert.False(ExtractionService.IsExtractionStale(gameDir, "dt", dataDir));

        // A game update changes the archive sizes.
        File.WriteAllText(Path.Combine(imageDir, "kh2_first.hed"), "updated-content-with-different-length");
        Assert.True(ExtractionService.IsExtractionStale(gameDir, "dt", dataDir));

        // No fingerprint recorded (old extraction) must not report stale.
        File.Delete(Path.Combine(dataDir, "kh2", ".source-fingerprint"));
        Assert.False(ExtractionService.IsExtractionStale(gameDir, "dt", dataDir));
        Directory.Delete(root, true);
    }
}

public class MovieServiceTests
{
    [Fact]
    public void SkipAndRestoreRenameTheMovieFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        var zmovie = Path.Combine(root, "STEAM", "juefigs", "KH2ReSource", "zmovie");
        Directory.CreateDirectory(Path.Combine(zmovie, "en"));
        File.WriteAllText(Path.Combine(zmovie, "en", "opn.mp4"), "fake");

        Assert.False(MovieService.AreMoviesSkipped(root));

        MovieService.SkipMovies(root);
        Assert.True(MovieService.AreMoviesSkipped(root));
        Assert.False(Directory.Exists(zmovie));
        Assert.True(File.Exists(Path.Combine(zmovie + ".disabled", "en", "opn.mp4")));
        MovieService.SkipMovies(root); // idempotent

        MovieService.RestoreMovies(root);
        Assert.False(MovieService.AreMoviesSkipped(root));
        Assert.True(File.Exists(Path.Combine(zmovie, "en", "opn.mp4")));
        MovieService.RestoreMovies(root); // idempotent

        Directory.Delete(root, true);
    }

    [Fact]
    public void ThrowsWhenMovieFolderMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.Throws<InvalidOperationException>(() => MovieService.SkipMovies(root));
        Directory.Delete(root, true);
    }
}

public class SikarugirTests
{
    private static string MakeWrapper(string root, string name, bool withPrefix = true)
    {
        var app = Path.Combine(root, name + ".app");
        var prefix = Path.Combine(app, "Contents", "SharedSupport", "prefix");
        Directory.CreateDirectory(withPrefix ? Path.Combine(prefix, "drive_c") : Path.Combine(app, "Contents"));
        return app;
    }

    [Fact]
    public void DiscoverWrappers_FindsWrapperPrefixesOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wrapper = MakeWrapper(root, "KH Steam");
        MakeWrapper(root, "NotAWrapper", withPrefix: false);

        var found = SikarugirApp.DiscoverWrappers(new[] { root });

        var bottle = Assert.Single(found);
        Assert.Equal("KH Steam", bottle.Name);
        Assert.Equal(WinePlatform.Sikarugir, bottle.Platform);
        Assert.Equal(wrapper, bottle.WrapperApp);
        Assert.True(Directory.Exists(bottle.DriveC));
        Directory.Delete(root, true);
    }

    [Fact]
    public void Resolve_PrefersStoredWrapperApp()
    {
        var root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wrapper = MakeWrapper(root, "KH Steam");

        var config = new AppConfig { BottleName = "KH Steam", WrapperApp = wrapper };
        var bottle = Bottle.Resolve(config);

        Assert.Equal(WinePlatform.Sikarugir, bottle.Platform);
        Assert.Equal(Path.Combine(wrapper, "Contents", "SharedSupport", "prefix"), bottle.Root);
        Directory.Delete(root, true);
    }
}
