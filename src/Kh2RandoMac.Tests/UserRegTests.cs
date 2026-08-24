using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

public class UserRegTests
{
    private static readonly string[] Overrides = { "version", "dinput8", "LuaBackend" };

    private static string SampleReg => string.Join('\n', new[]
    {
        "WINE REGISTRY Version 2",
        ";; All keys relative to \\\\User\\\\S-1-5-21-0-0-0-1000",
        "",
        "[Control Panel\\\\Desktop] 1700000000",
        "\"LogPixels\"=dword:00000060",
        "",
        "[Software\\\\Valve\\\\Steam] 1700000000",
        "\"AutoLoginUser\"=\"someone\"",
        "",
    });

    private static string RegWithEmptyOverridesSection => string.Join('\n', new[]
    {
        "WINE REGISTRY Version 2",
        "",
        "[Software\\\\Wine\\\\DllOverrides] 1700000000",
        "#time=1d000000000000",
        "",
        "[Software\\\\Valve\\\\Steam] 1700000000",
        "\"AutoLoginUser\"=\"someone\"",
        "",
    });

    private static FakeBottle BottleWithReg(string content)
    {
        var fake = new FakeBottle();
        File.WriteAllText(fake.Bottle.UserReg, content);
        return fake;
    }

    [Fact]
    public void CreatesSection_WhenMissing()
    {
        using var fake = BottleWithReg(SampleReg);
        fake.Bottle.EnsureDllOverrides(Overrides);
        var overrides = fake.Bottle.GetDllOverrides();
        Assert.Equal("native,builtin", overrides["version"]);
        Assert.Equal("native,builtin", overrides["dinput8"]);
        Assert.Equal("native,builtin", overrides["LuaBackend"]);
    }

    [Fact]
    public void InsertsSectionInSortedPosition()
    {
        using var fake = BottleWithReg(SampleReg);
        fake.Bottle.EnsureDllOverrides(Overrides);
        var lines = File.ReadAllLines(fake.Bottle.UserReg);
        var wineIdx = Array.FindIndex(lines, l => l.StartsWith("[Software\\\\Wine\\\\DllOverrides]"));
        var valveIdx = Array.FindIndex(lines, l => l.StartsWith("[Software\\\\Valve"));
        var desktopIdx = Array.FindIndex(lines, l => l.StartsWith("[Control Panel"));
        // Wine keeps sections sorted: Control Panel < Software\Valve < Software\Wine.
        Assert.True(desktopIdx < wineIdx, "Wine section should come after Control Panel");
        Assert.True(valveIdx < wineIdx, "Software\\Valve should sort before Software\\Wine");
    }

    [Fact]
    public void FillsExistingEmptySection()
    {
        using var fake = BottleWithReg(RegWithEmptyOverridesSection);
        fake.Bottle.EnsureDllOverrides(Overrides);
        var overrides = fake.Bottle.GetDllOverrides();
        Assert.Equal(3, overrides.Count);
        // Values must land inside the section, not after the blank separator (Wine would drop them).
        var lines = File.ReadAllLines(fake.Bottle.UserReg).ToList();
        var section = lines.FindIndex(l => l.StartsWith("[Software\\\\Wine\\\\DllOverrides]"));
        var next = lines.FindIndex(section + 1, l => l.StartsWith("["));
        var blank = lines.FindIndex(section + 1, string.IsNullOrWhiteSpace);
        var valueIdx = lines.FindIndex(section + 1, l => l.Contains("\"version\""));
        Assert.InRange(valueIdx, section + 1, next - 1);
        Assert.True(valueIdx < blank || blank > next, "values must come before the section's trailing blank line");
    }

    [Fact]
    public void DoesNotDowngradeExistingValue_AndIsIdempotent()
    {
        using var fake = BottleWithReg(SampleReg);
        fake.Bottle.EnsureDllOverrides(Overrides);
        var once = File.ReadAllText(fake.Bottle.UserReg);
        fake.Bottle.EnsureDllOverrides(Overrides);
        var twice = File.ReadAllText(fake.Bottle.UserReg);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void ReplacesDisabledOverride()
    {
        var reg = RegWithEmptyOverridesSection.Replace("#time=1d000000000000",
            "#time=1d000000000000\n\"version\"=\"builtin\"");
        using var fake = BottleWithReg(reg);
        fake.Bottle.EnsureDllOverrides(Overrides);
        Assert.Equal("native,builtin", fake.Bottle.GetDllOverrides()["version"]);
        // No duplicate entries.
        var count = File.ReadAllLines(fake.Bottle.UserReg).Count(l => l.TrimStart().StartsWith("\"version\""));
        Assert.Equal(1, count);
    }

    [Fact]
    public void WritesBackupBeforeEditing()
    {
        using var fake = BottleWithReg(SampleReg);
        fake.Bottle.EnsureDllOverrides(Overrides);
        Assert.True(File.Exists(fake.Bottle.UserReg + ".kh2rando.bak"));
        Assert.Equal(SampleReg, File.ReadAllText(fake.Bottle.UserReg + ".kh2rando.bak"));
    }

    [Fact]
    public void CreatesUserRegWhenMissing()
    {
        using var fake = new FakeBottle();
        Assert.False(File.Exists(fake.Bottle.UserReg));
        fake.Bottle.EnsureDllOverrides(Overrides);
        Assert.True(File.Exists(fake.Bottle.UserReg));
        Assert.Equal(3, fake.Bottle.GetDllOverrides().Count);
    }

    [Fact]
    public void PreservesUnrelatedContent()
    {
        using var fake = BottleWithReg(SampleReg);
        fake.Bottle.EnsureDllOverrides(Overrides);
        var text = File.ReadAllText(fake.Bottle.UserReg);
        Assert.Contains("\"AutoLoginUser\"=\"someone\"", text);
        Assert.Contains("\"LogPixels\"=dword:00000060", text);
        Assert.StartsWith("WINE REGISTRY Version 2", text);
    }

    [Fact]
    public void RemoveDllOverrides_RoundTripsCleanly()
    {
        using var fake = BottleWithReg(SampleReg);
        fake.Bottle.EnsureDllOverrides(Overrides);
        Assert.Equal(3, fake.Bottle.GetDllOverrides().Count);

        fake.Bottle.RemoveDllOverrides(Overrides);
        Assert.Empty(fake.Bottle.GetDllOverrides());
        var text = File.ReadAllText(fake.Bottle.UserReg);
        Assert.Contains("\"AutoLoginUser\"=\"someone\"", text);
        Assert.Contains("\"LogPixels\"=dword:00000060", text);
        // Idempotent on a second call and safe with no user.reg at all.
        fake.Bottle.RemoveDllOverrides(Overrides);
        using var empty = new FakeBottle();
        empty.Bottle.RemoveDllOverrides(Overrides);
    }
}
