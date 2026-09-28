using BenchmarkDotNet.Attributes;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Benchmarks;

/// <summary>
/// Hot-path queries against one size-4 world (2,562 tiles, seed 42):
/// tile lookup, adjacency, region/local derivation, spatial index.
/// </summary>
[MemoryDiagnoser]
public class QueryBenchmarks
{
    private Core.World _world = null!;
    private RegionHandle _region = null!;
    private GeoCoord _query;
    private GeoCoord _center;
    private readonly int[] _neighbors = new int[6];

    [GlobalSetup]
    public void Setup()
    {
        _world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var store = _world.Map.DataStore;
        _query = store.GetGeoCoord(100);
        _center = store.GetGeoCoord(0);
        _region = _world.GetRegion(7);
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();

    [Benchmark(Description = "GetTileIndex")]
    public int GetTileIndex() => _world.Map.DataStore.GetTileIndex(_query);

    [Benchmark(Description = "GetTileIndexExact")]
    public int GetTileIndexExact() => _world.Map.DataStore.GetTileIndexExact(_query);

    [Benchmark(Description = "GetAdjacent")]
    public int GetAdjacent() => _world.Map.DataStore.GetAdjacent(100, _neighbors);

    [Benchmark(Description = "DeriveRegionMap")]
    public int DeriveRegionMap() => _world.GetRegion(7).Cells.Length;

    [Benchmark(Description = "DeriveLocalMap")]
    public int DeriveLocalMap() => _region.GetLocal(3).Tiles.Length;

    [Benchmark(Description = "SpatialIndex_Build")]
    public int SpatialIndexBuild()
    {
        var store = _world.Map.DataStore;
        var index = new SpatialIndex(store, _world.Rivers, _world.WaterBodies, _world.Ranges, _world.Deposits);
        return index.TilesOfKind(FeatureKind.River).Count;
    }

    [Benchmark(Description = "QueryRadius_500km")]
    public int QueryRadius() => _world.QueryRadius(_center, 500.0).Count;

    [Benchmark(Description = "NearestRiver")]
    public double NearestRiver() => _world.NearestRiver(_query)?.DistanceKm ?? -1.0;

    [Benchmark(Description = "ScoreCitySites")]
    public int ScoreCitySites() => _world.ScoreCitySites().Count;
}
