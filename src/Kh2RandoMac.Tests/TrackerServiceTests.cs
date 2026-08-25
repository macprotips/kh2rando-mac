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

    [Theory]
    [InlineData("Wine Mono Windows Support", true)]
    [InlineData("Wine Mono Runtime", true)]
    [InlineData("CrossOver Mono", true)]
    [InlineData("CXHTML|CrossOver HTML engine", false)]
    [InlineData("Steam", false)]
    [InlineData("Harmonograph Deluxe", false)]
    public void IsMonoPackage_MatchesWineAndCrossOverVariantsOnly(string name, bool expected)
    {
        Assert.Equal(expected, TrackerService.IsMonoPackage(name));
    }

    [Fact]
    public void ReadSetupLogSummary_FindsVerdictInNewestLog()
    {
        using var fake = new FakeBottle();
        var temp = Path.Combine(fake.Bottle.DriveC, "users", "crossover", "AppData", "Local", "Temp");
        Directory.CreateDirectory(temp);
        Assert.Null(TrackerService.ReadSetupLogSummary(fake.Bottle));

        File.WriteAllText(Path.Combine(temp, "Microsoft .NET Framework 4.8 Setup_20260825_1.html"),
            "<html>Final Result: Installation completed successfully with success code: (0x00000000)</html>");
        var summary = TrackerService.ReadSetupLogSummary(fake.Bottle);
        Assert.NotNull(summary);
        Assert.StartsWith("Final Result: Installation completed successfully", summary);
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
