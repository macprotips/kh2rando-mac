using Kh2RandoMac.Core;
using OpenKh.Patcher;

namespace Kh2RandoMac.Tests;

public class PatchBuilderTests
{
    [Fact]
    public void NormalizePathSeparators_FixesWindowsPathsEverywhere()
    {
        // Real-world shape (GeminiHero Roxas mod): backslash and forward-slash asset
        // paths mixed in one mod, with nested sources and multi entries.
        var assets = new List<AssetFile>
        {
            new()
            {
                Name = @"obj\ACTOR_SORA.mdlx",
                Multi = new List<Multi> { new() { Name = @"obj\ACTOR_SORA_H.mdlx" } },
                Source = new List<AssetFile> { new() { Name = @"obj\ACTOR_SORA.mdlx" } },
            },
            new()
            {
                Name = "msg/us/al.bar",
                Source = new List<AssetFile>
                {
                    new() { Name = "al", Source = new List<AssetFile> { new() { Name = @"msg\al.yml" } } },
                },
            },
        };

        PatchBuilder.NormalizePathSeparators(assets);

        Assert.Equal("obj/ACTOR_SORA.mdlx", assets[0].Name);
        Assert.Equal("obj/ACTOR_SORA_H.mdlx", assets[0].Multi![0].Name);
        Assert.Equal("obj/ACTOR_SORA.mdlx", assets[0].Source![0].Name);
        Assert.Equal("msg/us/al.bar", assets[1].Name);
        Assert.Equal("al", assets[1].Source![0].Name);
        Assert.Equal("msg/al.yml", assets[1].Source![0].Source![0].Name);
    }
}

public class BuildAtomicityTests : IDisposable
{
    private readonly string _root;
    private readonly Workspace _workspace;

    public BuildAtomicityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kh2rando-tests", Guid.NewGuid().ToString("N"));
        _workspace = new Workspace(_root);
        _workspace.EnsureDirectories();
        Directory.CreateDirectory(_workspace.DataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Build_KeepsThePreviousBuildWhenAModIsUnreadable()
    {
        // A previous build the user is relying on.
        File.WriteAllText(Path.Combine(_workspace.CompiledModDir, "from-last-build.bin"), "keep me");

        // An enabled mod whose mod.yml declares no assets, which Build refuses. A
        // folder with no mod.yml at all would not count as installed and never reach it.
        var modDir = _workspace.ModPath("broken");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Combine(modDir, "mod.yml"), "title: Broken\n");
        _workspace.SaveEnabledMods(new[] { "broken" });

        Assert.Throws<InvalidOperationException>(() => new PatchBuilder(_workspace).Build());

        // The old build is still there, and no staging folder was left behind.
        Assert.True(File.Exists(Path.Combine(_workspace.CompiledModDir, "from-last-build.bin")));
        Assert.False(Directory.Exists(_workspace.CompiledModDir + ".building"));
    }

    [Fact]
    public void Build_WithNoModsReplacesThePreviousBuildCleanly()
    {
        File.WriteAllText(Path.Combine(_workspace.CompiledModDir, "from-last-build.bin"), "stale");
        _workspace.SaveEnabledMods(Array.Empty<string>());

        new PatchBuilder(_workspace).Build();

        Assert.False(File.Exists(Path.Combine(_workspace.CompiledModDir, "from-last-build.bin")));
        Assert.True(File.Exists(Path.Combine(_workspace.CompiledModDir, "patch-package-map.txt")));
        Assert.False(Directory.Exists(_workspace.CompiledModDir + ".building"));
    }
}
