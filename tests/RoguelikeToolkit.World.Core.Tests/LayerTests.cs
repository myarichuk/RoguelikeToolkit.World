using Xunit;
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Geometry;
using System;
using System.Diagnostics;
using SharpArena.Allocators;

namespace RoguelikeToolkit.World.Core.Tests;

public class LayerTests
{
    private struct DummyData { public int value; }

    private class DummyLayer : IMapLayer<DummyData>
    {
        public WorldDataStore Store { get; }
        public DummyLayer(WorldDataStore store) { Store = store; }
        public DummyData GetValue(GeoCoord coord) => Store.GetRef<DummyData>(Store.GetTileIndex(coord));
        public void Dispose() { }
    }


        [Fact]
    public void WorldMap_GetLayer_ZeroAllocation()
    {
        var map = new WorldMap(1);
        map.DataStore.RegisterLayer<DummyData>();
        var layer = new DummyLayer(map.DataStore);
        map.RegisterLayer(layer);
        map.DataStore.Allocate();

        // Warm up JIT
        var _ = map.GetLayer<DummyData>();

        long memBefore = GC.GetAllocatedBytesForCurrentThread();
        var result = map.GetLayer<DummyData>();
        long memAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.NotNull(result);
        Assert.Same(layer, result);
        Assert.Equal(memBefore, memAfter);

        map.Dispose();
    }





}
