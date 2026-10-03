using Xunit;
using RoguelikeToolkit.World.Core;
using System;
using System.Diagnostics;
using SharpArena.Allocators;

namespace RoguelikeToolkit.World.Core.Tests;

// Wall-clock ratio test inside: never overlap with other CPU-heavy classes in the same collection.
[Collection("CpuTiming")]
public class LayerTests
{
    #pragma warning disable CS0649 // Populated via WorldDataStore memory mapping, not direct assignment.
    private struct DummyData { public int value; }
    #pragma warning restore CS0649

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
        using var arena = ArenaDefaults.Create();
        var map = new WorldMap(5);
        using var layer = new TectonicPlateLayer(map.DataStore, 5, seed: 1234, arena: arena); // size 5 -> 252 tiles
        map.RegisterLayer(layer);
        map.DataStore.Allocate();

        // Let the JIT warm up the methods to ensure static init and JIT compilation
        // don't skew the results
        using var warmupArena = ArenaDefaults.Create();
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


    [Fact]
    public void TectonicPlateGeneration_RepeatedExecute_PerfSmoke_NoSignificantRegression()
    {
        // Keep this as a smoke test: large enough to exercise hot path, permissive enough for CI jitter.
        const int size = 5;
        const int seedCount = 128;

        PrewarmTopology(size);

        using var arena = ArenaDefaults.Create();
        using var map = new WorldMap(size);
        using var layer = new TectonicPlateLayer(map.DataStore, seedCount, seed: 1234, arena: arena);

        map.RegisterLayer(layer);
        map.DataStore.Allocate();

        var stage = new TectonicPlateGenerationStage(seedCount, 1234, arena);

        // JIT warmup
        using (var warmupMap = new WorldMap(2))
        {
            using var warmupLayer = new TectonicPlateLayer(warmupMap.DataStore, 2, 1234, arena);
            warmupMap.RegisterLayer(warmupLayer);
            warmupMap.DataStore.Allocate();
            stage.Execute(warmupMap);
        }

        long TimeExecuteTicks()
        {
            var sw = Stopwatch.StartNew();
            stage.Execute(map);
            sw.Stop();
            return sw.ElapsedTicks;
        }

        long first = TimeExecuteTicks();

        const int repeatRuns = 5;
        var repeats = new long[repeatRuns];
        for (int i = 0; i < repeatRuns; i++)
        {
            repeats[i] = TimeExecuteTicks();
        }

        // Median, not mean: xUnit runs other CPU-heavy classes in parallel, and one
        // contended run must not read as a regression (this failed ~8% of full-suite
        // runs on main with the mean). A systematic slowdown moves the median; a
        // scheduling spike does not.
        Array.Sort(repeats);
        double repeatedMedian = repeats[repeatRuns / 2];

        Assert.True(
            repeatedMedian <= first * 1.35,
            $"Repeated executes regressed too much: first={first} ticks, repeated median={repeatedMedian:F2} ticks");
    }

    private static void PrewarmTopology(int size)
    {
        const int seedCount = 8;

        using var arena = ArenaDefaults.Create();
        using var map = new WorldMap(size);
        using var layer = new TectonicPlateLayer(map.DataStore, seedCount, seed: 1, arena: arena);
        map.RegisterLayer(layer);
        map.DataStore.Allocate();

        using var stage = new TectonicPlateGenerationStage(seedCount, 1, arena);
        stage.Execute(map);
    }

}
