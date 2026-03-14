using Xunit;
using RoguelikeToolkit.World.Core;
using System;

namespace RoguelikeToolkit.World.Core.Tests;

public class OverlayTests
{
    private struct DummyData { public int value; }

    [Fact]
    public void OverlayManager_GetOverlaysAt_ZeroAllocation()
    {
        var manager = new OverlayManager();
        var overlay = new TectonicPlateOverlay(1, 1);
        manager.Register(overlay);

        var coord = new GeoCoord(0, 0);
        object[] backing = new object[4];
        Span<object> buffer = backing;

        long memBefore = GC.GetAllocatedBytesForCurrentThread();
        int count = manager.GetOverlaysAt(coord, buffer);
        long memAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(1, count);
        Assert.Same(overlay, buffer[0]);
        Assert.Equal(memBefore, memAfter);

        overlay.Dispose();
    }

    [Fact]
    public void TectonicPlateOverlay_Generate_ZeroAllocation()
    {
        using var arena = new SharpArena.Allocators.ArenaAllocator();
        using var overlay = new TectonicPlateOverlay(5, 5, seed: 1234, arena: arena); // size 5 -> 252 tiles

        // Let the JIT warm up the methods to ensure static init and JIT compilation
        // don't skew the results
        using var warmupArena = new SharpArena.Allocators.ArenaAllocator();
        using var warmupOverlay = new TectonicPlateOverlay(1, 1, 1, warmupArena);
        warmupOverlay.Generate();

        long memBefore = GC.GetAllocatedBytesForCurrentThread();
        overlay.Generate();
        long memAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(memBefore, memAfter);

        // Check if all tiles were processed
        var span = overlay.Store.GetSpan();
        for (int i = 0; i < span.Length; i++)
        {
            Assert.True(span[i].Id > 0, $"Tile {i} has uninitialized ID");
        }
    }
}
