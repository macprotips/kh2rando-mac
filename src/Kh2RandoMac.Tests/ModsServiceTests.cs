using System.IO.Compression;
using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class TempWorkspace : IDisposable
{
    public Workspace Workspace { get; }
    public string Root { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Workspace = new Workspace(Root);
        Workspace.EnsureDirectories();
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { }
    }
}

public class ModsServiceTests
{
    private static string MakeModZip(string dir, string name, params (string path, string content)[] extraFiles)
    {
        var zipPath = Path.Combine(dir, name + ".zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        WriteEntry(zip, "mod.yml", "title: Test Seed\ngame: kh2\nassets: []\n");
        foreach (var (path, content) in extraFiles)
            WriteEntry(zip, path, content);
        return zipPath;
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    [Fact]
    public void InstallFromZip_ExtractsAndEnables()
    {
        using var temp = new TempWorkspace();
        var zip = MakeModZip(temp.Root, "my-seed", ("files/data.bin", "hello"));
        var mods = new ModsService(temp.Workspace);
        mods.InstallFromZip(zip);
        mods.SetEnabled("my-seed", true);

        var list = mods.List();
        var mod = Assert.Single(list);
        Assert.Equal("my-seed", mod.Name);
        Assert.True(mod.Enabled);
        Assert.Equal("Test Seed", mod.Metadata?.Title);
        Assert.True(File.Exists(Path.Combine(temp.Workspace.ModPath("my-seed"), "files", "data.bin")));
    }

    [Fact]
    public void InstallFromZip_RejectsNonModZip()
    {
        using var temp = new TempWorkspace();
        var zipPath = Path.Combine(temp.Root, "junk.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            WriteEntry(zip, "readme.txt", "not a mod");
        Assert.Throws<InvalidOperationException>(() => new ModsService(temp.Workspace).InstallFromZip(zipPath));
    }

    [Fact]
    public void InstallFromZip_BlocksZipSlip()
    {
        using var temp = new TempWorkspace();
        var zipPath = Path.Combine(temp.Root, "evil.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteEntry(zip, "mod.yml", "title: evil\nassets: []\n");
            WriteEntry(zip, "../../escape.txt", "gotcha");
        }
        Assert.Throws<InvalidOperationException>(() => new ModsService(temp.Workspace).InstallFromZip(zipPath));
        Assert.False(File.Exists(Path.Combine(temp.Root, "escape.txt")));
        Assert.False(File.Exists(Path.Combine(temp.Root, "mods", "escape.txt")));
    }

    [Fact]
    public void InstallFromGit_RejectsMalformedNames()
    {
        using var temp = new TempWorkspace();
        var mods = new ModsService(temp.Workspace);
        Assert.Throws<ArgumentException>(() => mods.InstallFromGit("not-a-repo"));
        Assert.Throws<ArgumentException>(() => mods.InstallFromGit("a/b/c"));
        Assert.Throws<ArgumentException>(() => mods.InstallFromGit("../etc/passwd"));
        Assert.Throws<ArgumentException>(() => mods.InstallFromGit("a/.."));
    }

    [Fact]
    public void ModPath_RejectsTraversal()
    {
        using var temp = new TempWorkspace();
        Assert.Throws<ArgumentException>(() => temp.Workspace.ModPath("../outside"));
        Assert.Throws<ArgumentException>(() => temp.Workspace.ModPath("/absolute"));
    }

    [Fact]
    public void EnableDisable_KeepsOrderAndPersists()
    {
        using var temp = new TempWorkspace();
        var mods = new ModsService(temp.Workspace);
        mods.InstallFromZip(MakeModZip(temp.Root, "mod-a"));
        mods.InstallFromZip(MakeModZip(temp.Root, "mod-b"));

        mods.SetEnabled("mod-a", true);
        mods.SetEnabled("mod-b", true); // newest goes on top
        Assert.Equal(new[] { "mod-b", "mod-a" }, temp.Workspace.EnabledMods());

        mods.SetEnabled("mod-b", false);
        Assert.Equal(new[] { "mod-a" }, temp.Workspace.EnabledMods());
    }

    [Fact]
    public void Remove_CleansEnabledListAndAuthorFolder()
    {
        using var temp = new TempWorkspace();
        var mods = new ModsService(temp.Workspace);
        // Simulate a git-installed mod: author/repo layout.
        var modDir = Path.Combine(temp.Workspace.ModsDir, "SomeAuthor", "SomeMod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "mod.yml"), "title: X\nassets: []\n");
        mods.SetEnabled("SomeAuthor/SomeMod", true);

        mods.Remove("SomeAuthor/SomeMod");
        Assert.Empty(temp.Workspace.EnabledMods());
        Assert.False(Directory.Exists(Path.Combine(temp.Workspace.ModsDir, "SomeAuthor")));
    }

    [Fact]
    public void List_ShowsEnabledFirstInOrder()
    {
        using var temp = new TempWorkspace();
        var mods = new ModsService(temp.Workspace);
        mods.InstallFromZip(MakeModZip(temp.Root, "aaa"));
        mods.InstallFromZip(MakeModZip(temp.Root, "bbb"));
        mods.InstallFromZip(MakeModZip(temp.Root, "ccc"));
        mods.SetEnabled("ccc", true);
        mods.SetEnabled("aaa", true);

        var names = mods.List().Select(m => m.Name).ToList();
        Assert.Equal("aaa", names[0]);
        Assert.Equal("ccc", names[1]);
        Assert.Contains("bbb", names.Skip(2));
    }
}

public class ModOrderTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public ModOrderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        _workspace = new Workspace(_root);
        _workspace.EnsureDirectories();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private void MakeMod(string name)
    {
        var dir = _workspace.ModPath(name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mod.yml"), "title: " + name);
    }

    [Fact]
    public void List_WithoutASavedOrder_PutsEnabledFirstAsItAlwaysHas()
    {
        MakeMod("alpha");
        MakeMod("beta");
        MakeMod("gamma");
        _workspace.SaveEnabledMods(new[] { "gamma" });

        var names = new ModsService(_workspace).List().Select(m => m.Name).ToList();
        Assert.Equal("gamma", names[0]);
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void List_HonoursTheSavedOrderIncludingDisabledMods()
    {
        MakeMod("alpha");
        MakeMod("beta");
        MakeMod("gamma");
        // beta is disabled but sits between two enabled mods.
        _workspace.SaveModOrder(new[] { "gamma", "beta", "alpha" });
        _workspace.SaveEnabledMods(new[] { "gamma", "alpha" });

        var names = new ModsService(_workspace).List().Select(m => m.Name).ToList();
        Assert.Equal(new[] { "gamma", "beta", "alpha" }, names);
    }

    [Fact]
    public void List_AppendsModsTheSavedOrderHasNotSeenYet()
    {
        MakeMod("alpha");
        MakeMod("beta");
        _workspace.SaveModOrder(new[] { "beta" });
        _workspace.SaveEnabledMods(Array.Empty<string>());

        var names = new ModsService(_workspace).List().Select(m => m.Name).ToList();
        Assert.Equal(new[] { "beta", "alpha" }, names);
    }

    [Fact]
    public void SetEnabled_KeepsAModWhereItSatRatherThanJumpingItToTheTop()
    {
        MakeMod("alpha");
        MakeMod("beta");
        MakeMod("gamma");
        _workspace.SaveModOrder(new[] { "alpha", "beta", "gamma" });
        _workspace.SaveEnabledMods(new[] { "alpha", "gamma" });

        var service = new ModsService(_workspace);
        service.SetEnabled("beta", true);

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, _workspace.EnabledMods());
        Assert.Equal(new[] { "alpha", "beta", "gamma" },
            service.List().Select(m => m.Name).ToArray());
    }

    [Fact]
    public void SetEnabled_OffThenOnReturnsAModToItsOriginalPlace()
    {
        MakeMod("alpha");
        MakeMod("beta");
        MakeMod("gamma");
        _workspace.SaveModOrder(new[] { "alpha", "beta", "gamma" });
        _workspace.SaveEnabledMods(new[] { "alpha", "beta", "gamma" });

        var service = new ModsService(_workspace);
        service.SetEnabled("beta", false);
        service.SetEnabled("beta", true);

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, _workspace.EnabledMods());
    }
}

public class UnfinishedDownloadTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public UnfinishedDownloadTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        _workspace = new Workspace(_root);
        _workspace.EnsureDirectories();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>What the app quitting mid-clone leaves: a folder holding only .git.</summary>
    private string MakeStub(string repo)
    {
        var path = _workspace.ModPath(repo);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        return path;
    }

    [Fact]
    public void IsModInstalled_IsFalseForTheWreckageOfAnUnfinishedDownload()
    {
        MakeStub("KH-ReFined/KH2-MAIN");
        Assert.False(_workspace.IsModInstalled("KH-ReFined/KH2-MAIN"));
    }

    [Fact]
    public void IsModInstalled_IsTrueOnceThereIsAModYml()
    {
        var path = MakeStub("KH-ReFined/KH2-MAIN");
        File.WriteAllText(Path.Combine(path, "mod.yml"), "title: Re:Fined\nassets: []\n");
        Assert.True(_workspace.IsModInstalled("KH-ReFined/KH2-MAIN"));
    }

    [Fact]
    public void InstallFromGit_RefusesOnlyWhenTheModIsReallyThere()
    {
        var path = _workspace.ModPath("Some/Mod");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "mod.yml"), "title: X\nassets: []\n");

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ModsService(_workspace).InstallFromGit("Some/Mod"));
        Assert.Contains("already installed", ex.Message);
    }

    [Fact]
    public void InstallFromGit_ClearsAnUnfinishedDownloadInsteadOfRefusing()
    {
        MakeStub("Some/Mod");
        var messages = new List<string>();

        // The clone itself will fail (no such repo), but only after the stub is gone:
        // the old code never got that far, refusing on the folder's existence alone.
        Assert.ThrowsAny<Exception>(
            () => new ModsService(_workspace).InstallFromGit("Some/Mod", messages.Add));

        Assert.Contains(messages, m => m.Contains("unfinished earlier download"));
        Assert.DoesNotContain(messages, m => m.Contains("already installed"));
        Assert.False(Directory.Exists(_workspace.ModPath("Some/Mod")));
    }
}
