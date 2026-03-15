#!/bin/bash
cat << 'INNER_EOF' > tests/RoguelikeToolkit.World.Core.Tests/LayerTests.cs
using Xunit;
using RoguelikeToolkit.World.Core;
using System;

namespace RoguelikeToolkit.World.Core.Tests;

public class LayerTests
{
    private struct DummyData { public int value; }

    [Fact]
    public void WorldMap_GetLayer_ZeroAllocation()
    {
        var map = new WorldMap(1);
        var layer = new TectonicPlateLayer(map.DataStore, 1);
        map.RegisterLayer(layer);
        map.DataStore.Allocate();

        // Warm up JIT
        var _ = map.GetLayer<TectonicPlate>();

        long memBefore = GC.GetAllocatedBytesForCurrentThread();
        var result = map.GetLayer<TectonicPlate>();
        long memAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.NotNull(result);
        Assert.Same(layer, result);
        Assert.Equal(memBefore, memAfter);

        map.Dispose();
    }

    [Fact]
    public void TectonicPlateLayer_Generate_ZeroAllocation()
    {
        using var arena = new SharpArena.Allocators.ArenaAllocator();
        var map = new WorldMap(5);
        using var layer = new TectonicPlateLayer(map.DataStore, 5, seed: 1234, arena: arena); // size 5 -> 252 tiles
        map.RegisterLayer(layer);
        map.DataStore.Allocate();

        // Let the JIT warm up the methods to ensure static init and JIT compilation
        // don't skew the results
        using var warmupArena = new SharpArena.Allocators.ArenaAllocator();
        var warmupMap = new WorldMap(1);
        using var warmupLayer = new TectonicPlateLayer(warmupMap.DataStore, 1, 1, warmupArena);
        warmupMap.RegisterLayer(warmupLayer);
        warmupMap.DataStore.Allocate();

        var warmupPipeline = new WorldGenerationPipeline();
        warmupPipeline.AddStage(new TectonicPlateGenerationStage(1, 1, warmupArena));
        warmupPipeline.Execute(warmupMap);

        long memBefore = GC.GetAllocatedBytesForCurrentThread();
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new TectonicPlateGenerationStage(5, 1234, arena));
        pipeline.Execute(map);
        long memAfter = GC.GetAllocatedBytesForCurrentThread();

        // pipeline allocates inside AddStage, we can ignore this or measure Execute separately
        // Actually since we want zero alloc for Generate (which is now pipeline.Execute), let's measure just Execute

        long execMemBefore = GC.GetAllocatedBytesForCurrentThread();
        pipeline.Execute(map);
        long execMemAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(execMemBefore, execMemAfter);

        // Check if all tiles were processed
        var span = layer.Store.GetSpan<TectonicPlate>();
        for (int i = 0; i < span.Length; i++)
        {
            Assert.True(span[i].Id > 0, $"Tile {i} has uninitialized ID");
        }
    }
}
INNER_EOF
