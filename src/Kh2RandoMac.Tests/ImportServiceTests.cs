using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class ImportServiceTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _source;
    private readonly Workspace _target;

    public ImportServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        _source = new Workspace(Path.Combine(_root, "source"));
        _target = new Workspace(Path.Combine(_root, "target"));
        _source.EnsureDirectories();
        _target.EnsureDirectories();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static void FakeMod(Workspace ws, string name, string payload)
    {
        var dir = Path.Combine(ws.ModsDir, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.Combine(dir, "nested"));
        File.WriteAllText(Path.Combine(dir, "mod.yml"), "title: fake");
        File.WriteAllText(Path.Combine(dir, "nested", "data.txt"), payload);
    }

    [Fact]
    public void ExportThenImport_RestoresModsAndLoadOrderOnAnotherWorkspace()
    {
        FakeMod(_source, "author/one", "one");
        FakeMod(_source, "two", "two");
        _source.SaveEnabledMods(new[] { "two", "author/one" });
        var box = Path.Combine(_root, "box");
        Directory.CreateDirectory(box);
        var made = ExportService.Export(_source, box);

        Assert.Equal(FolderKind.Export, ImportService.Identify(made));
        var count = ImportService.Import(_target, made, applyLoadOrder: true);

        Assert.Equal(2, count);
        Assert.Equal(new[] { "author/one", "two" }, _target.InstalledMods().OrderBy(m => m));
        Assert.Equal("one", File.ReadAllText(
            Path.Combine(_target.ModPath("author/one"), "nested", "data.txt")));
        Assert.Equal(new[] { "two", "author/one" }, _target.EnabledMods());
    }

    [Fact]
    public void Import_ReplacesAClashingModAndKeepsTheOldOrderAsBackup()
    {
        FakeMod(_source, "one", "new");
        _source.SaveEnabledMods(new[] { "one" });
        var box = Path.Combine(_root, "box");
        Directory.CreateDirectory(box);
        var made = ExportService.Export(_source, box);

        FakeMod(_target, "one", "old");
        _target.SaveEnabledMods(new[] { "something-else" });
        ImportService.Import(_target, made, applyLoadOrder: true);

        Assert.Equal("new", File.ReadAllText(Path.Combine(_target.ModPath("one"), "nested", "data.txt")));
        Assert.Equal(new[] { "one" }, _target.EnabledMods());
        Assert.Equal("something-else",
            File.ReadAllText(_target.EnabledModsFile + ".bak").Trim());
    }

    [Fact]
    public void Identify_TellsTheThreeShapesApart()
    {
        var mod = Path.Combine(_root, "amod");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "mod.yml"), "title: x");
        Assert.Equal(FolderKind.SingleMod, ImportService.Identify(mod));

        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.Equal(FolderKind.Unknown, ImportService.Identify(empty));
        Assert.Equal(FolderKind.Unknown, ImportService.Identify(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void ImportSingleMod_InstallsAPlainModFolder()
    {
        var mod = Path.Combine(_root, "CoolMod");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "mod.yml"), "title: cool");

        var name = ImportService.ImportSingleMod(_target, mod);

        Assert.Equal("CoolMod", name);
        Assert.Contains("CoolMod", _target.InstalledMods());
    }

    [Fact]
    public void Import_RefusesAFolderInsideTheWorkspace_TheWipeEverythingCase()
    {
        // Dragging in the app's own mods folder used to delete every file it was
        // about to copy and then report success.
        FakeMod(_target, "author/one", "keep me");
        FakeMod(_target, "two", "keep me too");

        Assert.Throws<InvalidOperationException>(
            () => ImportService.Import(_target, _target.ModsDir, applyLoadOrder: false));
        Assert.Throws<InvalidOperationException>(
            () => ImportService.ImportSingleMod(_target, _target.ModPath("two")));

        Assert.Equal(new[] { "author/one", "two" }, _target.InstalledMods().OrderBy(m => m));
        Assert.Equal("keep me", File.ReadAllText(
            Path.Combine(_target.ModPath("author/one"), "nested", "data.txt")));
    }

    [Fact]
    public void Import_LeavesNoStagingFolderBehind()
    {
        FakeMod(_source, "one", "new");
        var box = Path.Combine(_root, "box");
        Directory.CreateDirectory(box);
        var made = ExportService.Export(_source, box);

        ImportService.Import(_target, made, applyLoadOrder: false);

        Assert.Empty(Directory.GetDirectories(_target.ModsDir, "*.importing", SearchOption.AllDirectories));
        Assert.Equal(new[] { "one" }, _target.InstalledMods());
    }
}
