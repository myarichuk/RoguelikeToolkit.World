using BenchmarkDotNet.Attributes;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Benchmarks;

/// <summary>
/// Minimal CI smoke set: proves the benchmarks build and run without timing
/// out. CI invokes this with a single launch/warmup/iteration; it is NOT a
/// baseline source (full numbers come from local runs, see README).
/// </summary>
[MemoryDiagnoser]
public class SmokeBenchmarks
{
    private Core.World _world = null!;
    private GeoCoord _query;

    [GlobalSetup]
    public void Setup()
    {
        _world = new WorldBuilder().WithSize(2).WithSeed(42).Build();
        _query = _world.Map.DataStore.GetGeoCoord(10);
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(Description = "Smoke_FullBuild_Size3")]
    public int SmokeFullBuild()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();
        return world.TileCount;
    }

    [Benchmark(Description = "Smoke_GetTileIndex")]
    public int SmokeGetTileIndex() => _world.Map.DataStore.GetTileIndex(_query);
}
