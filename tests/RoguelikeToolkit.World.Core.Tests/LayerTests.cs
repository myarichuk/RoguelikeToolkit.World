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
        var layer = new TectonicPlateLayer(1, 1);
        map.RegisterLayer(layer);

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
        using var layer = new TectonicPlateLayer(5, 5, seed: 1234, arena: arena); // size 5 -> 252 tiles

        // Let the JIT warm up the methods to ensure static init and JIT compilation
        // don't skew the results
        using var warmupArena = new SharpArena.Allocators.ArenaAllocator();
        using var warmupLayer = new TectonicPlateLayer(1, 1, 1, warmupArena);
        warmupLayer.Generate();

        long memBefore = GC.GetAllocatedBytesForCurrentThread();
        layer.Generate();
        long memAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(memBefore, memAfter);

        // Check if all tiles were processed
        var span = layer.Store.GetSpan();
        for (int i = 0; i < span.Length; i++)
        {
            Assert.True(span[i].Id > 0, $"Tile {i} has uninitialized ID");
        }
    }
}
