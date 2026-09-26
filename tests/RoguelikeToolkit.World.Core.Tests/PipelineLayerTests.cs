using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Regression tests for the "layer of type X is not registered" family:
/// the pipeline must fail fast naming the stage and layer instead of
/// throwing a bare ArgumentException from deep inside a stage.
/// </summary>
public class PipelineLayerTests
{
    /// <summary>
    /// Mimics the visualizer's old setup: plate/elevation/local layers only,
    /// then a full Discover() whose stages also write hydrology and climate.
    /// </summary>
    private static WorldMap MapWithAppLayers(int size)
    {
        var map = new WorldMap(size);
        var plates = new TectonicPlateLayer(map.DataStore, 4, seed: 42);
        map.RegisterLayer(plates);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<LocalMapInfo>(new LocalMapLayer(map.DataStore, 42, plates));
        map.DataStore.Allocate();
        return map;
    }

    private static WorldMap MapWithFullLayers(int size, int seed = 42, int seedCount = 4)
    {
        var map = new WorldMap(size);
        var plates = new TectonicPlateLayer(map.DataStore, seedCount, seed);
        map.RegisterLayer(plates);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<LocalMapInfo>(new LocalMapLayer(map.DataStore, seed, plates));
        map.DataStore.Allocate();
        return map;
    }

    [Fact]
    public void DiscoveredPipeline_MissingWrittenLayer_FailsFastWithStageAndLayer()
    {
        using var map = MapWithAppLayers(1);
        var pipeline = new WorldGenerationPipeline();
        pipeline.Discover();

        var ex = Assert.Throws<InvalidOperationException>(() => pipeline.Execute(map));
        // ClimateStage (order 16) is the first stage with a missing write layer.
        Assert.Contains("ClimateStage", ex.Message);
        Assert.Contains("ClimateInfo", ex.Message);
        Assert.Contains("RegisterLayer", ex.Message);
    }

    [Fact]
    public void DiscoveredPipeline_FullLayerSet_Executes()
    {
        using var map = MapWithFullLayers(1);
        var pipeline = new WorldGenerationPipeline();
        pipeline.Discover();
        foreach (var seeded in System.Linq.Enumerable.OfType<ISeededStage>(pipeline.Stages))
            seeded.Seed = 42;

        pipeline.Execute(map);

        Assert.True(map.DataStore.GetSpan<ElevationInfo>().Length > 0);
        Assert.True(map.DataStore.GetSpan<HydrologyInfo>().Length > 0);
        Assert.True(map.DataStore.GetSpan<ClimateInfo>().Length > 0);
    }

    [Fact]
    public void Execute_OptionalReadsMissing_StillRuns()
    {
        // LocalMapGenerationStage declares ClimateInfo as an optional read with
        // a built-in moisture fallback; absence of the layer must stay legal.
        using var map = new WorldMap(1);
        var plates = new TectonicPlateLayer(map.DataStore, 4, seed: 42);
        map.RegisterLayer(plates);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<LocalMapInfo>(new LocalMapLayer(map.DataStore, 42, plates));
        map.DataStore.Allocate();

        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new TectonicPlateGenerationStage(4, 42));
        pipeline.AddStage(new ElevationGenerationStage(42));
        pipeline.AddStage(new LocalMapGenerationStage(42));

        pipeline.Execute(map);
        Assert.NotEmpty(map.DataStore.GetSpan<LocalMapInfo>().ToArray());
    }

    [Fact]
    public void GetSpan_MissingLayer_HintsAtRegisterLayer()
    {
        using var map = new WorldMap(1);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();

        var ex = Assert.Throws<ArgumentException>(() => map.DataStore.GetSpan<HydrologyInfo>());
        Assert.Contains("RegisterLayer", ex.Message);
    }
}
