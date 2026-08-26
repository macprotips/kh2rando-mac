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
