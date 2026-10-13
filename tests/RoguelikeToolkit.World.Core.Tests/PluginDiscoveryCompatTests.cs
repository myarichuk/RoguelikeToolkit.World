using RoguelikeToolkit.World.Core.Generation;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Assembly-based plugin discovery (the Unity/IL2CPP path: plugins are compiled into the build) and the
/// netstandard2.1 behavior of directory discovery.
/// </summary>
public class PluginDiscoveryCompatTests
{
    [Fact]
    public void DiscoverFrom_NoPlugins_DiscoversBuiltIns_LikeDiscover()
    {
        using var viaAssemblies = new WorldGenerationPipeline();
        var diagnostics = new List<StageDiscoveryDiagnostic>();
        viaAssemblies.DiscoverFrom(Array.Empty<System.Reflection.Assembly>(), diagnostics.Add);

        using var viaDiscover = new WorldGenerationPipeline();
        viaDiscover.Discover();

        Assert.Empty(diagnostics);
        Assert.NotEmpty(viaAssemblies.Stages);
        Assert.Equal(viaDiscover.Stages.Select(s => s.GetType()), viaAssemblies.Stages.Select(s => s.GetType()));
    }

    [Fact]
    public void DiscoverFrom_DeduplicatesAssemblies_AndMergesOnRepeat()
    {
        var core = typeof(IWorldGeneratorStage).Assembly;
        using var pipeline = new WorldGenerationPipeline();
        pipeline.DiscoverFrom(new[] { core, core });
        int count = pipeline.Stages.Count;
        pipeline.DiscoverFrom(new[] { core });

        Assert.Equal(count, pipeline.Stages.Count);
    }

    [Fact]
    public void DiscoverFrom_ScansGivenAssemblies()
    {
        // This test assembly deliberately holds a stage with Order 10 (tectonics' slot) for the duplicate-order
        // tests, so scanning it must trip the same duplicate check Discover applies to directory plugins.
        using var pipeline = new WorldGenerationPipeline();
        var ex = Assert.Throws<InvalidOperationException>(() => pipeline.DiscoverFrom(new[] { typeof(PluginDiscoveryCompatTests).Assembly }));
        Assert.Contains("Duplicate Order", ex.Message);
    }

    [Fact]
    public void DiscoverFrom_NullAssemblies_Throws()
    {
        using var pipeline = new WorldGenerationPipeline();
        Assert.Throws<ArgumentNullException>(() => pipeline.DiscoverFrom(null!));
    }

#if CORE_NETSTANDARD
    [Fact]
    public void Discover_WithDirectory_ReportsUnsupported_OnNetStandard()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"plugins-ns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var diagnostics = new List<StageDiscoveryDiagnostic>();
            using var pipeline = new WorldGenerationPipeline();
            pipeline.Discover(dir, diagnostics.Add);

            var diagnostic = Assert.Single(diagnostics);
            Assert.IsType<PlatformNotSupportedException>(diagnostic.Exception);
            Assert.Contains("DiscoverFrom", diagnostic.Message);
            Assert.NotEmpty(pipeline.Stages); // built-in stages are still discovered
        }
        finally { Directory.Delete(dir, true); }
    }
#endif
}
