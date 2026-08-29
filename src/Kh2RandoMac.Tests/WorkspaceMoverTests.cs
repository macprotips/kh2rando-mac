using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class WorkspaceMoverTests : IDisposable
{
    private readonly string _root;

    public WorkspaceMoverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string MakeWorkspace(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(dir, "data", "kh2"));
        Directory.CreateDirectory(Path.Combine(dir, "mods"));
        File.WriteAllText(Path.Combine(dir, "data", "kh2", "extracted.bin"), new string('x', 4096));
        File.WriteAllText(Path.Combine(dir, "mods-KH2.txt"), "SomeAuthor/SomeMod");
        return dir;
    }

    [Fact]
    public void MovesEverythingAndLeavesNothingBehind()
    {
        var from = MakeWorkspace("old");
        var to = Path.Combine(_root, "new");

        WorkspaceMover.Move(from, to);

        Assert.False(Directory.Exists(from));
        Assert.True(File.Exists(Path.Combine(to, "data", "kh2", "extracted.bin")));
        Assert.Equal("SomeAuthor/SomeMod", File.ReadAllText(Path.Combine(to, "mods-KH2.txt")));
    }

    [Fact]
    public void RefusesToMoveAWorkspaceInsideItself()
    {
        var from = MakeWorkspace("old");
        var inside = Path.Combine(from, "data", "nested");

        var ex = Assert.Throws<InvalidOperationException>(() => WorkspaceMover.Move(from, inside));
        Assert.Contains("inside the current workspace", ex.Message);
        Assert.True(Directory.Exists(from));
    }

    [Fact]
    public void RefusesADestinationThatAlreadyHasSomethingInIt()
    {
        var from = MakeWorkspace("old");
        var to = Path.Combine(_root, "occupied");
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(to, "someones-file.txt"), "do not clobber me");

        var ex = Assert.Throws<InvalidOperationException>(() => WorkspaceMover.Move(from, to));
        Assert.Contains("already has something in it", ex.Message);
        Assert.Equal("do not clobber me", File.ReadAllText(Path.Combine(to, "someones-file.txt")));
        Assert.True(Directory.Exists(from));
    }

    [Fact]
    public void MovingToWhereItAlreadyIsDoesNothing()
    {
        var from = MakeWorkspace("old");
        WorkspaceMover.Move(from, from);
        Assert.True(File.Exists(Path.Combine(from, "data", "kh2", "extracted.bin")));
    }

    [Fact]
    public void RefusesWhenThereIsNothingToMove()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => WorkspaceMover.Move(Path.Combine(_root, "missing"), Path.Combine(_root, "new")));
        Assert.Contains("nothing at", ex.Message);
    }

    [Fact]
    public void SizeOnDisk_AddsUpTheFilesAndSurvivesAMissingFolder()
    {
        var from = MakeWorkspace("old");
        Assert.True(WorkspaceMover.SizeOnDisk(from) >= 4096);
        Assert.Equal(0, WorkspaceMover.SizeOnDisk(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void SameVolume_IsTrueWithinOneDiskAndSurvivesAPathThatIsNotThereYet()
    {
        var from = MakeWorkspace("old");
        Assert.True(WorkspaceMover.SameVolume(from, Path.Combine(_root, "not-created-yet")));
    }
}

public class FreeSpaceTests
{
    [Fact]
    public void FreeSpace_ReportsSomethingPlausibleForAnExistingPath()
    {
        var free = WorkspaceMover.FreeSpace(Path.GetTempPath());
        Assert.True(free > 0, "no free space reported for the temp folder");
    }

    [Fact]
    public void FreeSpace_WorksForAFolderThatDoesNotExistYet()
    {
        // The destination of a move has not been created when this is asked.
        var notYet = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"), "deep");
        Assert.True(WorkspaceMover.FreeSpace(notYet) > 0);
    }
}
