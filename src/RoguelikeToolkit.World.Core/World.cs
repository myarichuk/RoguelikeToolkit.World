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
    /// <summary>Bucketed nearest-feature index, built after the catalogs are populated.</summary>
    public SpatialIndex Index { get; private set; } = null!;

    internal World(WorldMap map, int seed)
    {
        Map = map;
        Seed = seed;
    }

    /// <summary>(Re)builds the spatial index from the current store and catalogs.</summary>
    internal void RebuildIndex(SiteCatalog? sites = null)
    {
        Index = new SpatialIndex(Map.DataStore, Rivers, WaterBodies, Ranges, Deposits, sites);
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
        var biome = info.Biome;
        int danger = info.DangerLevel;
        if (options?.History != null && options.History.TryGetTileModifier(i, out var mod))
            danger += (int)MathF.Round(mod.DangerDelta);
        if (options?.Materialized != null
            && options.Materialized.TryGetOverride(MapAddress.ForPlanet(Seed, i), out var tileOverride)
            && tileOverride != null)
        {
            if (tileOverride.Biome.HasValue) biome = tileOverride.Biome.Value;
            if (tileOverride.DangerLevel.HasValue) danger = tileOverride.DangerLevel.Value;
        }
        return (biome, Math.Max(0, danger));
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
        => Index.NearestFeature(from, FeatureKind.WaterBody);

    /// <summary>Deposits on one tile (usually zero or one).</summary>
    public List<Deposit> GetDeposits(int worldTileIndex)
        => Deposits.AtTile(worldTileIndex);

    /// <summary>Nearest deposit of any (or the given) type.</summary>
    public (Deposit Deposit, double DistanceKm)? NearestDeposit(GeoCoord from, DepositType? type = null)
    {
        var hit = Index.NearestDeposit(from, type);
        if (hit == null) return null;
        foreach (var d in Deposits.AtTile(hit.Value.TileIndex))
        {
            if (type.HasValue && d.Type != type.Value) continue;
            return (d, hit.Value.DistanceKm);
        }
        return null;
    }

    /// <summary>Nearest river tile, or null when no rivers were generated.</summary>
    public (int TileIndex, double DistanceKm)? NearestRiver(GeoCoord from)
        => Index.NearestFeature(from, FeatureKind.River);

    /// <summary>Address of the planet hex containing this coordinate.</summary>
    public PlanetHex Resolve(GeoCoord coord)
        => new(Seed, Map.DataStore.GetTileIndex(coord));

    public RegionHandle GetRegion(int worldTileIndex, int regionSize = RegionMaps.DefaultRegionSize)
        => GetRegion(new PlanetHex(Seed, worldTileIndex), regionSize);

    public RegionHandle GetRegion(PlanetHex hex, int regionSize = RegionMaps.DefaultRegionSize)
    {
        if (hex.WorldSeed != Seed) throw new ArgumentException("Planet hex belongs to a different world.", nameof(hex));
        var store = Map.DataStore;
        if ((uint)hex.TileIndex >= (uint)store.TileCount) throw new IndexOutOfRangeException();
        var parent = TerrainOrientation.Sample(store, hex.TileIndex);
        var center = store.GetGeoCoord(hex.TileIndex);
        double radiusKm = MapBounds.ForPlanetTile(store, hex.TileIndex).RadiusKm;
        uint seed = MapSeeds.DeriveRegionSeed(Seed, hex.TileIndex);
        return RegionMaps.DeriveRegionMap(
            MapAddress.ForRegion(Seed, hex.TileIndex, -1), parent, center, radiusKm, seed, regionSize);
    }

    /// <summary>Local map for one region cell of a planet hex. Pass the region size the cell belongs to.</summary>
    public LocalMapHandle GetLocal(PlanetHex hex, int regionCellIndex, int localSize = RegionMaps.DefaultLocalSize, int regionSize = RegionMaps.DefaultRegionSize)
        => GetRegion(hex, regionSize).GetLocal(regionCellIndex, localSize);

    /// <summary>Local map for one region cell. Pass the region size the cell belongs to.</summary>
    public LocalMapHandle GetLocal(RegionRef region, int localSize = RegionMaps.DefaultLocalSize, int regionSize = RegionMaps.DefaultRegionSize)
    {
        if (region.WorldSeed != Seed) throw new ArgumentException("Region belongs to a different world.", nameof(region));
        return GetRegion(new PlanetHex(Seed, region.WorldTileIndex), regionSize).GetLocal(region.RegionCellIndex, localSize);
    }

    public List<CitySiteScore> ScoreCitySites(CitySiteFilter? filter = null, QueryOptions? options = null)
        => CitySiteScorer.Score(Map.DataStore, Rivers, WaterBodies, filter ?? new CitySiteFilter(), options, Seed);

    /// <summary>Everything attached to one hex (biome, river reach, water, ranges, glacier, deposits).</summary>
    public TileFeatureInfo GetTileFeatures(int worldTileIndex, QueryOptions? options = null, SiteCatalog? sites = null)
    {
        var address = MapAddress.ForPlanet(Seed, worldTileIndex);
        TileOverride? tileOverride = null;
        if (options?.Materialized?.TryGetOverride(address, out var found) == true)
            tileOverride = found;
        return TileFeatures.Query(Map.DataStore, worldTileIndex, Rivers, WaterBodies, Ranges, Deposits, sites, address, tileOverride);
    }

    /// <summary>Everything attached to the hex containing this coordinate.</summary>
    public TileFeatureInfo GetTileFeatures(GeoCoord coord, QueryOptions? options = null, SiteCatalog? sites = null)
        => GetTileFeatures(Map.DataStore.GetTileIndex(coord), options, sites);

    public string RiverToWkt(int riverId)
    {
        if ((uint)riverId >= (uint)Rivers.Rivers.Count)
            throw new ArgumentOutOfRangeException(nameof(riverId), riverId, $"Unknown river id {riverId}.");
        return GeoWkt.RiverToWkt(Rivers.Rivers[riverId], Map.DataStore);
    }
    public string WaterBodyToWkt(int bodyId)
    {
        if ((uint)bodyId >= (uint)WaterBodies.Bodies.Count)
            throw new ArgumentOutOfRangeException(nameof(bodyId), bodyId, $"Unknown water-body id {bodyId}.");
        return GeoWkt.WaterBodyToWkt(WaterBodies.Bodies[bodyId], Map.DataStore);
    }
    public string DepositToWkt(int depositId)
    {
        if ((uint)depositId >= (uint)Deposits.Deposits.Count)
            throw new ArgumentOutOfRangeException(nameof(depositId), depositId, $"Unknown deposit id {depositId}.");
        return GeoWkt.DepositToWkt(Deposits.Deposits[depositId], Map.DataStore);
    }

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
    private Action<ClimateStage>? _climateConfig;
    private Action<HydrologyStage>? _hydrologyConfig;

    /// <summary>
    /// Largest supported planet size (backlog A2, from the Phase 2
    /// benchmarks: size 7 builds in ~2s; size 8 extrapolates to ~20-30s and
    /// ~3.9M-vert meshes, so it stays batch-only and unsupported).
    /// </summary>
    public const int MaxSupportedSize = 7;

    public WorldBuilder WithSize(int size)
    {
        if (size < 0 || size > MaxSupportedSize)
            throw new ArgumentOutOfRangeException(nameof(size), size,
                $"Planet size must be 0..{MaxSupportedSize} (size 8+ is unsupported; see docs/ARCHITECTURE.md).");
        _size = size;
        return this;
    }
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
    /// <summary>Tweak the default climate stage (smoothing passes...).</summary>
    public WorldBuilder WithClimate(Action<ClimateStage> configure)
    {
        var prev = _climateConfig;
        _climateConfig = prev == null ? configure : s => { prev(s); configure(s); };
        return this;
    }
    /// <summary>Tweak the default hydrology stage (river threshold, max rivers...).</summary>
    public WorldBuilder WithHydrology(Action<HydrologyStage> configure)
    {
        var prev = _hydrologyConfig;
        _hydrologyConfig = prev == null ? configure : s => { prev(s); configure(s); };
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
        var platesLayer = new TectonicPlateLayer(map.DataStore, _seedCount, _seed);
        map.RegisterLayer<TectonicPlate>(platesLayer);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<LocalMapInfo>(new LocalMapLayer(map.DataStore, _seed,
            platesLayer));
        map.DataStore.Allocate();

        var pipeline = new WorldGenerationPipeline();
        HydrologyStage? hydrologyStage = null;
        if (_useDefaults)
        {
            var tectonics = new TectonicPlateGenerationStage(_seedCount, _seed);
            _tectonicsConfig?.Invoke(tectonics);
            pipeline.AddStage(tectonics);
            pipeline.AddStage(new ElevationGenerationStage(_seed));
            var climate = new ClimateStage(_seed);
            _climateConfig?.Invoke(climate);
            pipeline.AddStage(climate);
            pipeline.AddStage(new ErosionGenerationStage());
            hydrologyStage = new HydrologyStage();
            _hydrologyConfig?.Invoke(hydrologyStage);
            pipeline.AddStage(hydrologyStage!);
            var biomes = new LocalMapGenerationStage(_seed);
            _biomesConfig?.Invoke(biomes);
            pipeline.AddStage(biomes);
        }
        foreach (var s in _extraStages) pipeline.AddStage(s);

        pipeline.Execute(map);

        var world = new World(map, _seed);
        HydrologyStage.PopulateCatalogs(map, world.Rivers, world.WaterBodies, hydrologyStage?.MaxRivers ?? new HydrologyStage().MaxRivers);
        RangeCatalogBuilder.Populate(map, world.Ranges);
        DepositCatalogBuilder.Populate(map, world.Deposits, _seed);
        world.RebuildIndex();
        return world;
    }
}
