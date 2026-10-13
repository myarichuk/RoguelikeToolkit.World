using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;
using System.Diagnostics;

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
        var map = new WorldMap(5);
        using var layer = new TectonicPlateLayer(map.DataStore, 5, seed: 1234); // size 5 -> 252 tiles
        map.RegisterLayer(layer);
        map.DataStore.Allocate();

        // Let the JIT warm up the methods to ensure static init and JIT compilation
        // don't skew the results
        var warmupMap = new WorldMap(1);
        using var warmupLayer = new TectonicPlateLayer(warmupMap.DataStore, 1, 1);
        warmupMap.RegisterLayer(warmupLayer);
        warmupMap.DataStore.Allocate();

        var warmupPipeline = new WorldGenerationPipeline();
        warmupPipeline.AddStage(new TectonicPlateGenerationStage(1, 1));
        warmupPipeline.Execute(warmupMap);

        long memBefore = GC.GetAllocatedBytesForCurrentThread();
        var pipeline = new WorldGenerationPipeline();
        pipeline.AddStage(new TectonicPlateGenerationStage(5, 1234));
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
        using var map = new WorldMap(size);
        using var layer = new TectonicPlateLayer(map.DataStore, seedCount, seed: 1234);

        map.RegisterLayer(layer);
        map.DataStore.Allocate();

        var stage = new TectonicPlateGenerationStage(seedCount, 1234);

        // JIT warmup
        using (var warmupMap = new WorldMap(2))
        {
            using var warmupLayer = new TectonicPlateLayer(warmupMap.DataStore, 2, 1234);
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

    [Fact]
    public void TectonicPlateGeneration_ReusedStage_MatchesFreshStage_AcrossMapSizes()
    {
        // The stage keeps grow-only scratch buffers. Running a larger map (and more plates)
        // first leaves them oversized with stale data; a smaller map afterwards must still
        // match a fresh stage bit for bit.
        static TectonicPlate[] Run(TectonicPlateGenerationStage stage, int size, int seedCount)
        {
            using var map = new WorldMap(size);
            using var layer = new TectonicPlateLayer(map.DataStore, seedCount, seed: 99);
            map.RegisterLayer(layer);
            map.DataStore.Allocate();
            stage.SeedCount = seedCount;
            stage.Execute(map);
            return map.DataStore.GetSpan<TectonicPlate>().ToArray();
        }

        var reused = new TectonicPlateGenerationStage(64, 99);
        Run(reused, 5, 64);
        var afterLarge = Run(reused, 3, 12);
        var fresh = Run(new TectonicPlateGenerationStage(12, 99), 3, 12);

        Assert.Equal(fresh, afterLarge);
    }

    private static void PrewarmTopology(int size)
    {
        const int seedCount = 8;
        using var map = new WorldMap(size);
        using var layer = new TectonicPlateLayer(map.DataStore, seedCount, seed: 1);
        map.RegisterLayer(layer);
        map.DataStore.Allocate();

        using var stage = new TectonicPlateGenerationStage(seedCount, 1);
        stage.Execute(map);
    }

}
