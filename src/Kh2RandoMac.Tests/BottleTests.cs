using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

/// <summary>Builds a throwaway bottle directory with real symlinks to exercise path translation.</summary>
public class FakeBottle : IDisposable
{
    public Bottle Bottle { get; }
    public string Root { get; }
    public string FakeHome { get; }

    public FakeBottle()
    {
        Root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        FakeHome = Path.Combine(Root, "fakehome");
        Directory.CreateDirectory(Path.Combine(Root, "drive_c"));
        Directory.CreateDirectory(Path.Combine(Root, "dosdevices"));
        Directory.CreateDirectory(FakeHome);
        File.CreateSymbolicLink(Path.Combine(Root, "dosdevices", "c:"), "../drive_c");
        File.CreateSymbolicLink(Path.Combine(Root, "dosdevices", "y:"), FakeHome);
        File.CreateSymbolicLink(Path.Combine(Root, "dosdevices", "z:"), "/");
        Bottle = new Bottle { Name = "test", Root = Root };
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { }
    }
}

public class BottleTests
{
    [Fact]
    public void DriveMappings_ResolvesSymlinks()
    {
        using var fake = new FakeBottle();
        var map = fake.Bottle.DriveMappings();
        Assert.Equal(Path.Combine(fake.Root, "drive_c"), map['C']);
        Assert.Equal(fake.FakeHome, map['Y']);
        Assert.Equal("/", map['Z']);
    }

    [Fact]
    public void ToWindowsPath_PrefersMostSpecificDrive()
    {
        using var fake = new FakeBottle();
        // Inside fake home → Y:, not Z:.
        var win = fake.Bottle.ToWindowsPath(Path.Combine(fake.FakeHome, "KH2 Rando", "mod"));
        Assert.Equal(@"Y:\KH2 Rando\mod", win);
    }

    [Fact]
    public void ToWindowsPath_FallsBackToRootDrive()
    {
        using var fake = new FakeBottle();
        var win = fake.Bottle.ToWindowsPath("/Volumes/SomeDrive/Games");
        Assert.Equal(@"Z:\Volumes\SomeDrive\Games", win);
    }

    [Fact]
    public void ToMacPath_RoundTrips()
    {
        using var fake = new FakeBottle();
        var mac = fake.Bottle.ToMacPath(@"Y:\KH2 Rando\mod");
        Assert.Equal(Path.Combine(fake.FakeHome, "KH2 Rando/mod"), mac);
    }

    [Fact]
    public void ToMacPath_UnknownDriveReturnsNull()
    {
        using var fake = new FakeBottle();
        Assert.Null(fake.Bottle.ToMacPath(@"Q:\nope"));
    }

    [Fact]
    public void CrossOverVersion_IsReadFromTheBottleConf()
    {
        using var fake = new FakeBottle();
        Assert.Null(fake.Bottle.CrossOverVersion);

        File.WriteAllLines(fake.Bottle.BottleConf, new[]
        {
            ";; Version              This is the CrossOver version that made the bottle",
            "\"Version\" = \"27.0.0.40817\"",
            "\"Template\" = \"win10_64\"",
        });

        // Stable and Preview can both be installed; a bottle upgraded by Preview fails
        // to run under the older stable build, so the version decides which one to use.
        Assert.Equal("27.0.0.40817", fake.Bottle.CrossOverVersion);
    }
}
