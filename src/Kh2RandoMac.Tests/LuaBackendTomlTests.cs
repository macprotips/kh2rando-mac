using Kh2RandoMac.Core;

namespace Kh2RandoMac.Tests;

/// <summary>Runs against the real stock LuaBackend.toml shipped in the v1.9.1 release.</summary>
public class LuaBackendTomlTests
{
    private static string StockToml => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestData-LuaBackend.toml"));

    [Fact]
    public void AddsModScriptsPathToKh2Section()
    {
        var result = LuaBackendService.PatchToml(StockToml, "Y:/KH2 Rando/mod/kh2/scripts", steam: false);
        var kh2 = result.Substring(result.IndexOf("[kh2]", StringComparison.Ordinal));
        var kh2Scripts = kh2.Substring(0, kh2.IndexOf("[bbs]", StringComparison.Ordinal));
        Assert.Contains("{path = \"Y:/KH2 Rando/mod/kh2/scripts\", relative = false}", kh2Scripts);
        // Only the kh2 section gets the extra path.
        Assert.Equal(1, CountOf(result, "relative = false"));
    }

    [Fact]
    public void SteamSwapsGameDocsOnlyInKh2Section()
    {
        var result = LuaBackendService.PatchToml(StockToml, "Y:/mods/kh2/scripts", steam: true);
        var kh2 = ExtractSection(result, "[kh2]", "[bbs]");
        Assert.Contains("game_docs = \"My Games/KINGDOM HEARTS HD 1.5+2.5 ReMIX\"", kh2);
        Assert.Contains("# game_docs = \"KINGDOM HEARTS HD 1.5+2.5 ReMIX\"", kh2);
        // Other sections untouched (kh1 keeps EGS active).
        var kh1 = ExtractSection(result, "[kh1]", "[kh2]");
        Assert.Contains("\ngame_docs = \"KINGDOM HEARTS HD 1.5+2.5 ReMIX\"", kh1);
    }

    [Fact]
    public void EgsLeavesGameDocsAlone()
    {
        var result = LuaBackendService.PatchToml(StockToml, "Y:/mods/kh2/scripts", steam: false);
        var kh2 = ExtractSection(result, "[kh2]", "[bbs]");
        Assert.Contains("\ngame_docs = \"KINGDOM HEARTS HD 1.5+2.5 ReMIX\"", kh2);
    }

    [Fact]
    public void ThrowsOnUnexpectedFormat()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LuaBackendService.PatchToml("[kh1]\nnothing here", "Y:/x", steam: false));
    }

    private static string ExtractSection(string text, string start, string end)
    {
        var s = text.IndexOf(start, StringComparison.Ordinal);
        var e = text.IndexOf(end, StringComparison.Ordinal);
        return text.Substring(s, e - s);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
            count++;
        return count;
    }
}

public class GameLocatorTests
{
    [Fact]
    public void ParsesLibraryFoldersVdf()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                }
                "1"
                {
                    "path"		"L:\\SteamLibrary"
                }
            }
            """;
        var paths = GameLocator.ParseLibraryFolders(vdf);
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"L:\SteamLibrary" }, paths);
    }

    [Fact]
    public void EmptyVdfYieldsNothing()
    {
        Assert.Empty(GameLocator.ParseLibraryFolders("\"libraryfolders\"\n{\n}\n"));
    }
}
