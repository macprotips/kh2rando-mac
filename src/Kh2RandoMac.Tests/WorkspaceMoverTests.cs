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

/// <summary>
/// Reset can delete the unpacked game data, tens of gigabytes of it, so the guard on
/// where it will do that is worth pinning down.
/// </summary>
public class ResetDataDeletionTests : IDisposable
{
    private readonly string _root;

    public ResetDataDeletionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private (AppConfig Config, Workspace Workspace) Setup(string workspaceRoot)
    {
        var workspace = new Workspace(workspaceRoot);
        workspace.EnsureDirectories();
        Directory.CreateDirectory(Path.Combine(workspace.DataDir, "kh2"));
        File.WriteAllText(Path.Combine(workspace.DataDir, "kh2", "extracted.bin"), "game data");
        return (new AppConfig { WorkspaceRoot = workspaceRoot }, workspace);
    }

    [Fact]
    public void DeletesTheExtractedDataForARealWorkspace()
    {
        var (config, workspace) = Setup(Path.Combine(_root, "KH2 Rando"));
        var messages = new List<string>();

        SetupService.DeleteExtractedData(config, messages.Add);

        Assert.False(Directory.Exists(workspace.DataDir));
        // Everything else in the workspace survives.
        Assert.True(Directory.Exists(workspace.ModsDir));
    }

    [Fact]
    public void RefusesWhenTheWorkspaceIsTheHomeFolder()
    {
        // A corrupt config once reset this app's workspace root; a recursive delete
        // aimed at a home folder is not a recoverable mistake.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = new AppConfig { WorkspaceRoot = home };
        var messages = new List<string>();

        SetupService.DeleteExtractedData(config, messages.Add);

        Assert.Contains(messages, m => m.StartsWith("WARNING: not deleting anything"));
        Assert.True(Directory.Exists(home));
    }

    [Fact]
    public void RefusesWhenTheWorkspaceIsAVolumeRoot()
    {
        var config = new AppConfig { WorkspaceRoot = "/" };
        var messages = new List<string>();

        SetupService.DeleteExtractedData(config, messages.Add);

        Assert.Contains(messages, m => m.StartsWith("WARNING: not deleting anything"));
        Assert.True(Directory.Exists("/"));
    }

    [Fact]
    public void SaysSoWhenThereIsNothingExtracted()
    {
        var workspaceRoot = Path.Combine(_root, "empty workspace");
        new Workspace(workspaceRoot).EnsureDirectories();
        Directory.Delete(new Workspace(workspaceRoot).DataDir, true);
        var messages = new List<string>();

        SetupService.DeleteExtractedData(new AppConfig { WorkspaceRoot = workspaceRoot }, messages.Add);

        Assert.Contains(messages, m => m.Contains("No extracted game data"));
    }
}

public class ResetDeletionSafetyTests : IDisposable
{
    private readonly string _root;

    public ResetDeletionSafetyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void DeletingTheDataFolderDoesNotFollowASymlinkOutOfIt()
    {
        // Someone with a small disk could plausibly symlink the extracted data onto
        // another drive. A recursive delete that walked through the link would take
        // whatever is on the far side with it.
        var precious = Path.Combine(_root, "somewhere else");
        Directory.CreateDirectory(precious);
        File.WriteAllText(Path.Combine(precious, "irreplaceable.txt"), "do not delete me");

        var workspaceRoot = Path.Combine(_root, "KH2 Rando");
        var workspace = new Workspace(workspaceRoot);
        workspace.EnsureDirectories();
        Directory.CreateSymbolicLink(Path.Combine(workspace.DataDir, "linked"), precious);

        SetupService.DeleteExtractedData(new AppConfig { WorkspaceRoot = workspaceRoot }, _ => { });

        Assert.False(Directory.Exists(workspace.DataDir));
        Assert.True(Directory.Exists(precious));
        Assert.Equal("do not delete me", File.ReadAllText(Path.Combine(precious, "irreplaceable.txt")));
    }

    [Fact]
    public void RefusesWhenTheDataFolderIsItselfALinkPointingOutOfTheWorkspace()
    {
        var elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "keep.txt"), "keep");

        var workspaceRoot = Path.Combine(_root, "linked workspace");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateSymbolicLink(Path.Combine(workspaceRoot, "data"), elsewhere);

        SetupService.DeleteExtractedData(new AppConfig { WorkspaceRoot = workspaceRoot }, _ => { });

        // Whatever happens to the link, the folder it points at must survive.
        Assert.True(Directory.Exists(elsewhere));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(elsewhere, "keep.txt")));
    }

    [Fact]
    public void LeavesModsAndSeedsAlone()
    {
        var workspaceRoot = Path.Combine(_root, "KH2 Rando");
        var workspace = new Workspace(workspaceRoot);
        workspace.EnsureDirectories();
        Directory.CreateDirectory(Path.Combine(workspace.ModsDir, "kh2", "Author", "Mod"));
        File.WriteAllText(Path.Combine(workspace.ModsDir, "kh2", "Author", "Mod", "mod.yml"), "title: X");
        File.WriteAllText(workspace.EnabledModsFile, "Author/Mod");
        Directory.CreateDirectory(workspace.DataDir);

        SetupService.DeleteExtractedData(new AppConfig { WorkspaceRoot = workspaceRoot }, _ => { });

        Assert.False(Directory.Exists(workspace.DataDir));
        Assert.True(File.Exists(Path.Combine(workspace.ModsDir, "kh2", "Author", "Mod", "mod.yml")));
        Assert.True(File.Exists(workspace.EnabledModsFile));
    }
}
