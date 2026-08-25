using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class TrackerServiceTests
{
    [Fact]
    public void ParseUninstallerList_ExtractsEntriesAndIgnoresWineNoise()
    {
        var output = string.Join('\n',
            "msync: bootstrapped mach port on wine-1a7737e-msync.",
            "msync: up and running.",
            "CXHTML|||CrossOver HTML engine",
            "Steam App 2552430|||KINGDOM HEARTS -HD 1.5+2.5 ReMIX-",
            "Steam|||Steam",
            "{D535B019-7406-58F2-8408-EA9A188713EF}|||Wine Mono Windows Support",
            "");
        var entries = TrackerService.ParseUninstallerList(output);
        Assert.Equal(4, entries.Count);
        var mono = Assert.Single(entries, e => e.Name.Contains("Wine Mono"));
        Assert.Equal("{D535B019-7406-58F2-8408-EA9A188713EF}", mono.Id);
    }

    [Fact]
    public void ParseUninstallerList_EmptyOutput_YieldsNothing()
    {
        Assert.Empty(TrackerService.ParseUninstallerList(""));
        Assert.Empty(TrackerService.ParseUninstallerList("just some log line\nanother"));
    }

    [Fact]
    public void HasDotNet48_RequiresClrDll_NotJustTheFolder()
    {
        using var fake = new FakeBottle();
        // Wine's mono stub creates the folder and a few files, but never clr.dll.
        var frameworkDir = Path.Combine(fake.Bottle.DriveC,
            "windows", "Microsoft.NET", "Framework64", "v4.0.30319");
        Directory.CreateDirectory(frameworkDir);
        File.WriteAllText(Path.Combine(frameworkDir, "mscorlib.dll"), "stub");
        Assert.False(TrackerService.HasDotNet48(fake.Bottle));

        File.WriteAllText(Path.Combine(frameworkDir, "clr.dll"), "real");
        Assert.True(TrackerService.HasDotNet48(fake.Bottle));
    }

    [Fact]
    public void IsInstalled_NeedsBothExeAndRuntime()
    {
        using var fake = new FakeBottle();
        var workspaceRoot = Path.Combine(fake.Root, "workspace");
        var workspace = new Workspace(workspaceRoot);
        Assert.False(TrackerService.IsInstalled(workspace, fake.Bottle));

        Directory.CreateDirectory(TrackerService.TrackerDir(workspace));
        File.WriteAllText(TrackerService.ExePath(workspace), "exe");
        Assert.False(TrackerService.IsInstalled(workspace, fake.Bottle));

        var frameworkDir = Path.Combine(fake.Bottle.DriveC,
            "windows", "Microsoft.NET", "Framework64", "v4.0.30319");
        Directory.CreateDirectory(frameworkDir);
        File.WriteAllText(Path.Combine(frameworkDir, "clr.dll"), "real");
        Assert.True(TrackerService.IsInstalled(workspace, fake.Bottle));
    }
}
