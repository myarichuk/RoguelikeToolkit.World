using BenchmarkDotNet.Attributes;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Benchmarks;

/// <summary>
/// Per-stage <c>Execute</c> cost on a size-3 world (642 tiles). Each stage gets
/// a dedicated map pre-baked through its prerequisites once in
/// <see cref="GlobalSetup"/>; the benchmark re-runs just that stage, so every
/// invocation repeats the same deterministic work.
/// </summary>
[MemoryDiagnoser]
public class StageBenchmarks
{
    private const int Size = 3;
    private const int Seed = 42;
    private const int SeedCount = 12;

    private WorldMap _tectonicsMap = null!;
    private WorldMap _elevationMap = null!;
    private WorldMap _climateMap = null!;
    private WorldMap _erosionMap = null!;
    private WorldMap _hydrologyMap = null!;
    private WorldMap _biomesMap = null!;

    private static WorldMap NewMap()
    {
        var map = new WorldMap(Size);
        var plates = new TectonicPlateLayer(map.DataStore, SeedCount, Seed);
        map.RegisterLayer<TectonicPlate>(plates);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<LocalMapInfo>(new LocalMapLayer(map.DataStore, Seed, plates));
        map.DataStore.Allocate();
        return map;
    }

    [GlobalSetup]
    public void Setup()
    {
        _tectonicsMap = NewMap();

        _elevationMap = NewMap();
        new TectonicPlateGenerationStage(SeedCount, Seed).Execute(_elevationMap);

        _climateMap = NewMap();
        new TectonicPlateGenerationStage(SeedCount, Seed).Execute(_climateMap);
        new ElevationGenerationStage(Seed).Execute(_climateMap);

        _erosionMap = NewMap();
        new TectonicPlateGenerationStage(SeedCount, Seed).Execute(_erosionMap);
        new ElevationGenerationStage(Seed).Execute(_erosionMap);
        new ClimateStage(Seed).Execute(_erosionMap);

        _hydrologyMap = NewMap();
        new TectonicPlateGenerationStage(SeedCount, Seed).Execute(_hydrologyMap);
        new ElevationGenerationStage(Seed).Execute(_hydrologyMap);
        new ClimateStage(Seed).Execute(_hydrologyMap);
        new ErosionGenerationStage().Execute(_hydrologyMap);

        _biomesMap = NewMap();
        new TectonicPlateGenerationStage(SeedCount, Seed).Execute(_biomesMap);
        new ElevationGenerationStage(Seed).Execute(_biomesMap);
        new ClimateStage(Seed).Execute(_biomesMap);
        new ErosionGenerationStage().Execute(_biomesMap);
        new HydrologyStage().Execute(_biomesMap);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _tectonicsMap.Dispose();
        _elevationMap.Dispose();
        _climateMap.Dispose();
        _erosionMap.Dispose();
        _hydrologyMap.Dispose();
        _biomesMap.Dispose();
    }

    [Benchmark(Description = "Tectonics")]
    public int Tectonics()
    {
        new TectonicPlateGenerationStage(SeedCount, Seed).Execute(_tectonicsMap);
        return _tectonicsMap.DataStore.TileCount;
    }

    [Benchmark(Description = "Elevation")]
    public int Elevation()
    {
        new ElevationGenerationStage(Seed).Execute(_elevationMap);
        return _elevationMap.DataStore.TileCount;
    }

    [Benchmark(Description = "Climate")]
    public int Climate()
    {
        new ClimateStage(Seed).Execute(_climateMap);
        return _climateMap.DataStore.TileCount;
    }

    [Benchmark(Description = "Erosion")]
    public int Erosion()
    {
        new ErosionGenerationStage().Execute(_erosionMap);
        return _erosionMap.DataStore.TileCount;
    }

    [Benchmark(Description = "Hydrology")]
    public int Hydrology()
    {
        new HydrologyStage().Execute(_hydrologyMap);
        return _hydrologyMap.DataStore.TileCount;
    }

    [Benchmark(Description = "Biomes")]
    public int Biomes()
    {
        new LocalMapGenerationStage(Seed).Execute(_biomesMap);
        return _biomesMap.DataStore.TileCount;
    }
}
