using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>One tile hit from a spatial query.</summary>
public struct TileHit
{
    public int TileIndex;
    public GeoCoord Coord;
    public float Elevation;
    public BiomeType Biome;
    public double DistanceKm;
}

/// <summary>
/// Public library facade over <see cref="WorldMap"/> plus managed feature
/// catalogs. Owns generation (via <see cref="WorldBuilder"/>) and all
/// game-facing queries. History is consumed here via <see cref="QueryOptions"/>.
/// </summary>
public sealed class World : IDisposable
{
    public const double EarthRadiusKm = 6371.0;

    public WorldMap Map { get; }
    public int Seed { get; }
    public int Size => Map.DataStore.Size;
    public int TileCount => Map.DataStore.TileCount;
    public RiverCatalog Rivers { get; } = new();
    public WaterBodyCatalog WaterBodies { get; } = new();
    public RangeCatalog Ranges { get; } = new();
    public DepositCatalog Deposits { get; } = new();

    internal World(WorldMap map, int seed)
    {
        Map = map;
        Seed = seed;
    }

    public float SampleElevation(GeoCoord coord)
    {
        int i = Map.DataStore.GetTileIndex(coord);
        return Map.DataStore.GetRef<ElevationInfo>(i).Height;
    }

    public (BiomeType Biome, int Danger) SampleBiome(GeoCoord coord, QueryOptions? options = null)
    {
        int i = Map.DataStore.GetTileIndex(coord);
        var info = Map.DataStore.GetRef<LocalMapInfo>(i);
        int danger = info.DangerLevel;
        if (options?.History != null && options.History.TryGetTileModifier(i, out var mod))
            danger += (int)MathF.Round(mod.DangerDelta);
        return (info.Biome, Math.Max(0, danger));
    }

    public static double DistanceKm(GeoCoord a, GeoCoord b)
    {
        var va = Vector3D.FromGeoCoord(a);
        var vb = Vector3D.FromGeoCoord(b);
        double dot = Math.Clamp(Vector3D.Dot(va, vb), -1.0, 1.0);
        return Math.Acos(dot) * EarthRadiusKm;
    }

    /// <summary>All tiles within radiusKm of center, sorted by distance.</summary>
    public List<TileHit> QueryRadius(GeoCoord center, double radiusKm, QueryOptions? options = null)
    {
        var store = Map.DataStore;
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        bool hasLocals = store.IsLayerRegistered<LocalMapInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        var locals = hasLocals ? store.GetSpan<LocalMapInfo>() : default;

        var result = new List<TileHit>();
        for (int i = 0; i < store.TileCount; i++)
        {
            var c = store.GetGeoCoord(i);
            double d = DistanceKm(center, c);
            if (d <= radiusKm)
            {
                result.Add(new TileHit
                {
                    TileIndex = i,
                    Coord = c,
                    Elevation = hasElev ? elev[i].Height : 0f,
                    Biome = hasLocals ? locals[i].Biome : BiomeType.Plains,
                    DistanceKm = d
                });
            }
        }
        result.Sort((a, b) => a.DistanceKm.CompareTo(b.DistanceKm));
        return result;
    }

    /// <summary>Nearest open water (sea or ponded lake).</summary>
    public (int TileIndex, double DistanceKm)? NearestWater(GeoCoord from)
    {
        var store = Map.DataStore;
        if (!store.IsLayerRegistered<ElevationInfo>()) return null;
        var elev = store.GetSpan<ElevationInfo>();
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < store.TileCount; i++)
        {
            bool water = elev[i].Height < ElevationGenerationStage.SeaLevel
                || (hasHydro && hydro[i].LakeDepth > 0f);
            if (!water) continue;
            double d = DistanceKm(from, store.GetGeoCoord(i));
            if (d < bestD) { bestD = d; best = i; }
        }
        return best < 0 ? null : (best, bestD);
    }

    /// <summary>Deposits on one tile (usually zero or one).</summary>
    public List<Deposit> GetDeposits(int worldTileIndex)
        => Deposits.AtTile(worldTileIndex);

    /// <summary>Nearest deposit of any (or the given) type.</summary>
    public (Deposit Deposit, double DistanceKm)? NearestDeposit(GeoCoord from, DepositType? type = null)
    {
        var store = Map.DataStore;
        Deposit? best = null;
        double bestD = double.MaxValue;
        foreach (var d in Deposits.Deposits)
        {
            if (type.HasValue && d.Type != type.Value) continue;
            double dist = DistanceKm(from, store.GetGeoCoord(d.TileIndex));
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best == null ? null : (best, bestD);
    }

    /// <summary>Nearest river tile, or null when no rivers were generated.</summary>
    public (int TileIndex, double DistanceKm)? NearestRiver(GeoCoord from)
    {
        if (Rivers.Rivers.Count == 0) return null;
        var store = Map.DataStore;
        int best = -1;
        double bestD = double.MaxValue;
        foreach (var r in Rivers.Rivers)
        {
            foreach (var t in r.Path)
            {
                double d = DistanceKm(from, store.GetGeoCoord(t));
                if (d < bestD) { bestD = d; best = t; }
            }
        }
        return best < 0 ? null : (best, bestD);
    }

    public RegionHandle GetRegion(int worldTileIndex, int regionSize = RegionMaps.DefaultRegionSize)
    {
        var store = Map.DataStore;
        if ((uint)worldTileIndex >= (uint)store.TileCount) throw new IndexOutOfRangeException();
        float h = store.IsLayerRegistered<ElevationInfo>() ? store.GetSpan<ElevationInfo>()[worldTileIndex].Height : 0f;
        BiomeType b = store.IsLayerRegistered<LocalMapInfo>() ? store.GetSpan<LocalMapInfo>()[worldTileIndex].Biome : BiomeType.Plains;
        float m = store.IsLayerRegistered<ClimateInfo>() ? store.GetSpan<ClimateInfo>()[worldTileIndex].Precipitation : 0.5f;
        return RegionMaps.DeriveRegion(Seed, worldTileIndex, h, b, m, regionSize);
    }

    public List<CitySiteScore> ScoreCitySites(CitySiteFilter? filter = null, QueryOptions? options = null)
        => CitySiteScorer.Score(Map.DataStore, Rivers, WaterBodies, filter ?? new CitySiteFilter(), options);

    /// <summary>Everything attached to one hex (biome, river reach, water, ranges, glacier, deposits).</summary>
    public TileFeatureInfo GetTileFeatures(int worldTileIndex)
        => TileFeatures.Query(Map.DataStore, worldTileIndex, Rivers, WaterBodies, Ranges, Deposits);

    /// <summary>Everything attached to the hex containing this coordinate.</summary>
    public TileFeatureInfo GetTileFeatures(GeoCoord coord)
        => GetTileFeatures(Map.DataStore.GetTileIndex(coord));

    public string RiverToWkt(int riverId) => GeoWkt.RiverToWkt(Rivers.Rivers[riverId], Map.DataStore);
    public string WaterBodyToWkt(int bodyId) => GeoWkt.WaterBodyToWkt(WaterBodies.Bodies[bodyId], Map.DataStore);
    public string DepositToWkt(int depositId) => GeoWkt.DepositToWkt(Deposits.Deposits[depositId], Map.DataStore);

    public void Dispose() => Map.Dispose();
}

/// <summary>Fluent builder for a fully generated <see cref="World"/>.</summary>
public sealed class WorldBuilder
{
    private int _size = 3;
    private int _seed = 42;
    private int _seedCount = 12;
    private string? _filePath;
    private readonly List<IWorldGeneratorStage> _extraStages = new();
    private bool _useDefaults = true;
    private Action<TectonicPlateGenerationStage>? _tectonicsConfig;
    private Action<LocalMapGenerationStage>? _biomesConfig;

    public WorldBuilder WithSize(int size) { _size = size; return this; }
    public WorldBuilder WithSeed(int seed) { _seed = seed; return this; }
    public WorldBuilder WithPlateCount(int seedCount) { _seedCount = seedCount; return this; }
    public WorldBuilder WithFilePath(string? path) { _filePath = path; return this; }
    public WorldBuilder AddStage(IWorldGeneratorStage stage) { _extraStages.Add(stage); return this; }
    /// <summary>Skip the default stage set; only <see cref="AddStage"/> stages run.</summary>
    public WorldBuilder WithoutDefaultStages() { _useDefaults = false; return this; }
    /// <summary>Select the plate area strategy (default: noisy flood-fill).</summary>
    public WorldBuilder WithPartitioner(IPlatePartitioner partitioner)
        => WithTectonics(t => t.Partitioner = partitioner);
    /// <summary>Tweak the default tectonics stage (partitioner, segmentation...).</summary>
    public WorldBuilder WithTectonics(Action<TectonicPlateGenerationStage> configure)
    {
        var prev = _tectonicsConfig;
        _tectonicsConfig = prev == null ? configure : s => { prev(s); configure(s); };
        return this;
    }
    /// <summary>Tweak the default biome stage (smoothing passes...).</summary>
    public WorldBuilder WithBiomes(Action<LocalMapGenerationStage> configure)
    {
        var prev = _biomesConfig;
        _biomesConfig = prev == null ? configure : s => { prev(s); configure(s); };
        return this;
    }

    public World Build()
    {
        var map = new WorldMap(_size, _filePath);
        // Field layers (dense, in-store). Feature catalogs attach to World, not the store.
        map.RegisterLayer<TectonicPlate>(new TectonicPlateLayer(map.DataStore, _seedCount, _seed));
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<LocalMapInfo>(new LocalMapLayer(map.DataStore, _seed,
            new TectonicPlateLayer(map.DataStore, _seedCount, _seed)));
        map.DataStore.Allocate();

        var pipeline = new WorldGenerationPipeline();
        if (_useDefaults)
        {
            var tectonics = new TectonicPlateGenerationStage(_seedCount, _seed);
            _tectonicsConfig?.Invoke(tectonics);
            pipeline.AddStage(tectonics);
            pipeline.AddStage(new ElevationGenerationStage(_seed));
            pipeline.AddStage(new ClimateStage(_seed));
            pipeline.AddStage(new ErosionGenerationStage());
            pipeline.AddStage(new HydrologyStage());
            var biomes = new LocalMapGenerationStage(_seed);
            _biomesConfig?.Invoke(biomes);
            pipeline.AddStage(biomes);
        }
        foreach (var s in _extraStages) pipeline.AddStage(s);

        pipeline.Execute(map);

        var world = new World(map, _seed);
        HydrologyStage.PopulateCatalogs(map, world.Rivers, world.WaterBodies);
        RangeCatalogBuilder.Populate(map, world.Ranges);
        DepositCatalogBuilder.Populate(map, world.Deposits, _seed);
        return world;
    }
}
