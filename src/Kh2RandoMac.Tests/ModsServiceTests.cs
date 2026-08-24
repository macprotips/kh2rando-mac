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
