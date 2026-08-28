using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class TrackerReadinessTests
{
    private static void WriteFramework(Bottle bottle)
    {
        var dir = Path.Combine(bottle.DriveC, "windows", "Microsoft.NET", "Framework64", "v4.0.30319");
        Directory.CreateDirectory(Path.Combine(dir, "WPF"));
        File.WriteAllText(Path.Combine(dir, "WPF", "wpfgfx_v0400.dll"), "renderer");
        using var clr = File.Create(Path.Combine(dir, "clr.dll"));
        clr.SetLength(6_000_000);
    }

    [Fact]
    public void MonoInstalled_ReadsTheRegistryWithoutStartingTheBottle()
    {
        using var fake = new FakeBottle();
        var systemReg = Path.Combine(fake.Bottle.Root, "system.reg");

        File.WriteAllText(systemReg, "WINE REGISTRY Version 2\n");
        Assert.False(TrackerService.MonoInstalled(fake.Bottle));

        // What CrossOver leaves behind after setting a bottle up again.
        File.AppendAllText(systemReg, "\"DisplayName\"=\"Wine Mono Windows Support\"\n");
        Assert.True(TrackerService.MonoInstalled(fake.Bottle));
    }

    [Fact]
    public void IsReady_IsFalseWhileWinesSubstituteIsInFront()
    {
        using var fake = new FakeBottle();
        var workspace = new Workspace(Path.Combine(fake.Root, "workspace"));
        workspace.EnsureDirectories();
        Directory.CreateDirectory(TrackerService.TrackerDir(workspace));
        File.WriteAllText(TrackerService.ExePath(workspace), "exe");
        WriteFramework(fake.Bottle);
        var systemReg = Path.Combine(fake.Bottle.Root, "system.reg");
        File.WriteAllText(systemReg, "WINE REGISTRY Version 2\n");

        // Framework present and nothing in front of it: good to go.
        Assert.True(TrackerService.IsInstalled(workspace, fake.Bottle));
        Assert.True(TrackerService.IsReady(workspace, fake.Bottle));

        // CrossOver reinstates its substitute. The files are all still there, so the
        // old check still says "installed" -- but the tracker would crash on startup.
        File.AppendAllText(systemReg, "\"DisplayName\"=\"Wine Mono Windows Support\"\n");
        Assert.True(TrackerService.IsInstalled(workspace, fake.Bottle));
        Assert.False(TrackerService.IsReady(workspace, fake.Bottle));
    }

    [Fact]
    public void MonoInstalled_IsFalseWhenTheBottleHasNoRegistry()
    {
        using var fake = new FakeBottle();
        Assert.False(TrackerService.MonoInstalled(fake.Bottle));
    }
}
