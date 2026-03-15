using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class PipelineTests
{
    [Fact]
    public void Discover_LoadsStagesInOrder()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.Discover();

        var stages = pipeline.Stages;

        Assert.True(stages.Count >= 2);

        var tectonicStage = stages.OfType<TectonicPlateGenerationStage>().FirstOrDefault();
        var localMapStage = stages.OfType<LocalMapGenerationStage>().FirstOrDefault();

        Assert.NotNull(tectonicStage);
        Assert.NotNull(localMapStage);

        var tectonicIndex = Array.IndexOf(stages.ToArray(), tectonicStage);
        var localMapIndex = Array.IndexOf(stages.ToArray(), localMapStage);

        Assert.True(tectonicIndex < localMapIndex);
    }

    [Fact]
    public void Discover_ThrowsWhenExistingAndDiscoveredStagesShareOrder()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new DuplicateOrderManualStage());

        var ex = Assert.Throws<InvalidOperationException>(() => pipeline.Discover());

        Assert.Contains("Duplicate Order values", ex.Message);
        Assert.Contains("10", ex.Message);
    }

    [Fact]
    public void Discover_ReportsPluginLoadFailuresThroughCallback()
    {
        var pluginDirectory = Path.Combine(Path.GetTempPath(), $"pipeline-load-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginDirectory);
        var badPlugin = Path.Combine(pluginDirectory, "invalid.dll");

        try
        {
            File.WriteAllText(badPlugin, "not a valid assembly");

            var diagnostics = new List<StageDiscoveryDiagnostic>();
            var pipeline = new WorldGenerationPipeline();

            pipeline.Discover(pluginDirectory, diagnostics.Add);

            Assert.Single(diagnostics);
            Assert.Contains("Failed to load plugin assembly", diagnostics[0].Message);
        }
        finally
        {
            Directory.Delete(pluginDirectory, recursive: true);
        }
    }

    [Fact]
    public void Discover_ThrowsAggregateExceptionForLoadFailuresWithoutCallback()
    {
        var pluginDirectory = Path.Combine(Path.GetTempPath(), $"pipeline-load-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pluginDirectory);
        var badPlugin = Path.Combine(pluginDirectory, "invalid.dll");

        try
        {
            File.WriteAllText(badPlugin, "not a valid assembly");

            var pipeline = new WorldGenerationPipeline();

            var ex = Assert.Throws<AggregateException>(() => pipeline.Discover(pluginDirectory));

            Assert.Single(ex.InnerExceptions);
        }
        finally
        {
            Directory.Delete(pluginDirectory, recursive: true);
        }
    }

    [Fact]
    public void Discover_MergesWithoutDuplicatesAcrossMultipleCalls()
    {
        var pipeline = new WorldGenerationPipeline();

        pipeline.Discover();
        var firstSet = pipeline.Stages.Select(stage => stage.GetType()).OrderBy(type => type.FullName).ToArray();

        pipeline.Discover();
        var secondSet = pipeline.Stages.Select(stage => stage.GetType()).OrderBy(type => type.FullName).ToArray();

        Assert.Equal(firstSet, secondSet);
    }

    [Fact]
    public void DiscoverAndReplace_RebuildsDeterministicDiscoveredStageSet()
    {
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new ManualStage());

        pipeline.DiscoverAndReplace();
        var discoveredOnlySet = pipeline.Stages.Select(stage => stage.GetType()).OrderBy(type => type.FullName).ToArray();

        pipeline.DiscoverAndReplace();
        var secondDiscoveredOnlySet = pipeline.Stages.Select(stage => stage.GetType()).OrderBy(type => type.FullName).ToArray();

        Assert.Equal(discoveredOnlySet, secondDiscoveredOnlySet);
        Assert.DoesNotContain(typeof(ManualStage), discoveredOnlySet);
    }

    [Fact]
    public void ResetStages_DisposesDisposableStages()
    {
        var pipeline = new WorldGenerationPipeline();
        var stage = new DisposableStage();
        pipeline.AddStage(stage);

        pipeline.ResetStages();

        Assert.True(stage.Disposed);
        Assert.Empty(pipeline.Stages);
    }

    [WorldGeneratorStage(10)]
    private sealed class DuplicateOrderManualStage : IWorldGeneratorStage
    {
        public void Execute(WorldMap map)
        {
        }
    }

    private sealed class ManualStage : IWorldGeneratorStage
    {
        public void Execute(WorldMap map)
        {
        }
    }

    private sealed class DisposableStage : IWorldGeneratorStage, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Execute(WorldMap map)
        {
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
