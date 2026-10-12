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

    // Kept so Recompute can re-run the stages downstream of an edit with the exact configuration
    // the world was built with.
    private WorldGenerationPipeline? _pipeline;
    private int _maxRivers = 64;
    private float _riverScale = 1f;

    internal World(WorldMap map, int seed)
    {
        Map = map;
        Seed = seed;
    }

    /// <summary>The astronomical parameters the climate was generated with (Earth unless <see cref="WorldBuilder.WithPlanet"/> said otherwise).</summary>
    public PlanetParameters Planet { get; internal set; } = new PlanetParameters();

    internal void Attach(WorldGenerationPipeline pipeline, int maxRivers, float riverScale)
    {
        _pipeline = pipeline;
        _maxRivers = maxRivers;
        _riverScale = riverScale;
    }

    /// <summary>True when the world was built with <see cref="WorldBuilder.WithUserEdits"/>.</summary>
    public bool Editable => _pipeline != null && Map.DataStore.IsLayerRegistered<UserEditInfo>();

    /// <summary>
    /// The per-tile hand edits (height sculpt/pin, biome pin). Write here, then call
    /// <see cref="Recompute"/>. Throws when the world was not built editable.
    /// </summary>
    public Span<UserEditInfo> Edits
        => Editable ? Map.DataStore.GetSpan<UserEditInfo>() : throw new InvalidOperationException("Build the world with WithUserEdits() to edit it.");

    /// <summary>
    /// Re-derives everything downstream of the hand edits: elevation (with the sculpt added), climate,
    /// erosion, hydrology and biomes, then the river/lake/range/deposit catalogs. Tectonics is not
    /// repeated. Pure in (seed, configuration, <see cref="Edits"/>): recomputing with the same edits
    /// reproduces the same world bit for bit, and with no edits it reproduces the generated one, so
    /// applying is idempotent and never double-erodes. Hydrology is global (a ridge can redirect a
    /// river basin away), so this is a whole-world action, not a per-brush-dab one.
    /// </summary>
    public void Recompute()
    {
        if (!Editable) throw new InvalidOperationException("Build the world with WithUserEdits() to recompute it.");
        _pipeline!.ExecuteFrom(Map, ElevationGenerationStage.PipelineOrder);
        Rivers.Rivers.Clear();
        WaterBodies.Bodies.Clear();
        Ranges.Features.Clear();
        Deposits.Deposits.Clear();
        PopulateFeatureCatalogs();
        RebuildIndex();
    }

    internal void PopulateFeatureCatalogs()
    {
        HydrologyStage.PopulateCatalogs(Map, Rivers, WaterBodies, _maxRivers);
        RangeCatalogBuilder.Populate(Map, Ranges, riverThresholdScale: _riverScale);
        DepositCatalogBuilder.Populate(Map, Deposits, Seed);
    }

    /// <summary>(Re)builds the spatial index from the current store and catalogs.</summary>
    internal void RebuildIndex(SiteCatalog? sites = null)
    {
        Index = new SpatialIndex(Map.DataStore, Rivers, WaterBodies, Ranges, Deposits, sites);
    }

    /// <summary>
    /// Deterministic seed for a planet tile, derived from the world seed and the tile index.
    /// Not stored per tile; recomputing it is cheaper than the 4 bytes per hex it would cost.
    /// </summary>
    public uint TileSeed(int tileIndex) => Rng.DeriveTileSeed(Seed, tileIndex);

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
        return DetMath.Acos(dot) * EarthRadiusKm;
    }

    /// <summary>All tiles within radiusKm of center, sorted by distance.</summary>
    public List<TileHit> QueryRadius(GeoCoord center, double radiusKm, QueryOptions? options = null)
    {
        var store = Map.DataStore;
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        bool hasLocals = store.IsLayerRegistered<LocalMapInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        var locals = hasLocals ? store.GetSpan<LocalMapInfo>() : default;

        // Compare dot products against cos(radius/R); only hits pay for Acos.
        var vectors = store.GetTileVectors();
        var target = Vector3D.FromGeoCoord(center);
        double angle = radiusKm / EarthRadiusKm;
        double minDot = angle >= Math.PI ? -2.0 : DetMath.Cos(angle);
        var result = new List<TileHit>();
        for (int i = 0; i < vectors.Length; i++)
        {
            double dot = Vector3D.Dot(target, vectors[i]);
            if (dot < minDot) continue;
            double d = DetMath.Acos(Math.Clamp(dot, -1.0, 1.0)) * EarthRadiusKm;
            if (d > radiusKm) continue;
            result.Add(new TileHit
            {
                TileIndex = i,
                Coord = store.GetGeoCoord(i),
                Elevation = hasElev ? elev[i].Height : 0f,
                Biome = hasLocals ? locals[i].Biome : BiomeType.Plains,
                DistanceKm = d
            });
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
    /// <summary>WKT polygon of the water body with this <see cref="WaterBody.Id"/> (the id carried by <see cref="River.TerminalBody"/> and <see cref="HydrologyInfo.WaterBodyId"/>).</summary>
    public string WaterBodyToWkt(int bodyId)
    {
        foreach (var body in WaterBodies.Bodies)
        {
            if (body.Id == bodyId) return GeoWkt.WaterBodyToWkt(body, Map.DataStore);
        }
        throw new ArgumentOutOfRangeException(nameof(bodyId), bodyId, $"Unknown water-body id {bodyId}.");
    }
    public string DepositToWkt(int depositId)
    {
        if ((uint)depositId >= (uint)Deposits.Deposits.Count)
            throw new ArgumentOutOfRangeException(nameof(depositId), depositId, $"Unknown deposit id {depositId}.");
        return GeoWkt.DepositToWkt(Deposits.Deposits[depositId], Map.DataStore);
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        Map.Dispose();
    }
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
    private bool _userEdits;
    private bool _climateRefresh = true;

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
    /// <summary>
    /// Sets the planet's axial tilt, stellar flux, spin and (optionally) a custom insolation field; see
    /// <see cref="PlanetParameters"/>. Earth by default. The parameters are copied, so later changes to
    /// <paramref name="planet"/> do not affect this builder.
    /// </summary>
    public WorldBuilder WithPlanet(PlanetParameters planet)
    {
        if (planet == null) throw new ArgumentNullException(nameof(planet));
        var copy = planet.Clone();
        return WithClimate(c => c.Planet = copy);
    }

    /// <summary>
    /// Re-runs the climate on the eroded relief before hydrology and biomes (see <see cref="ClimateRefreshStage"/>),
    /// so rain shadows and biome borders follow the mountains the player sees rather than the raw tectonic
    /// surface. On by default; <c>WithClimateRefresh(false)</c> restores the single-pass pipeline (pair it with
    /// <c>RiverThresholdScale = 1</c>, which is what that pipeline was calibrated at).
    /// <para>
    /// Why it is the default: erosion moves the coastline (sediment fills shallows, so a size-4 world gains
    /// around 20% more land tiles), and the single pass left every one of those tiles with the ocean's 0.55
    /// precipitation floor and no maritime-distance correction. The refresh measured on seven worlds: land
    /// 0.01-0.04 drier, 0.015-0.033 warmer, jungle 16% to 12% of land, desert 7% to 10%, glacier 13% to 10%;
    /// river count restored by the hydrology scale (0.92), lakes unchanged.
    /// </para>
    /// </summary>
    public WorldBuilder WithClimateRefresh(bool enabled = true) { _climateRefresh = enabled; return this; }

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

    /// <summary>
    /// Registers the <see cref="UserEditInfo"/> input layer so the result supports hand edits and
    /// <see cref="World.Recompute"/>. Costs 12 bytes per tile; an unedited layer changes nothing.
    /// </summary>
    public WorldBuilder WithUserEdits() { _userEdits = true; return this; }

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
        if (_userEdits) map.RegisterLayer<UserEditInfo>(new UserEditLayer(map.DataStore));
        map.DataStore.Allocate();

        var pipeline = new WorldGenerationPipeline();
        HydrologyStage? hydrologyStage = null;
        PlanetParameters planet = new PlanetParameters();
        float riverScale = 1f;
        if (_useDefaults)
        {
            var tectonics = new TectonicPlateGenerationStage(_seedCount, _seed);
            _tectonicsConfig?.Invoke(tectonics);
            pipeline.AddStage(tectonics);
            pipeline.AddStage(new ElevationGenerationStage(_seed));
            var climate = new ClimateStage(_seed);
            _climateConfig?.Invoke(climate);
            planet = climate.Planet ?? planet;
            pipeline.AddStage(climate);
            hydrologyStage = new HydrologyStage();
            _hydrologyConfig?.Invoke(hydrologyStage);
            riverScale = hydrologyStage.RiverThresholdScale;
            pipeline.AddStage(new ErosionGenerationStage { RiverThresholdScale = riverScale });
            if (_climateRefresh) pipeline.AddStage(new ClimateRefreshStage(climate)); // ties erosion's order; the stable sort keeps it after
            pipeline.AddStage(hydrologyStage);
            var biomes = new LocalMapGenerationStage(_seed) { RiverThresholdScale = riverScale };
            _biomesConfig?.Invoke(biomes);
            pipeline.AddStage(biomes);
        }
        foreach (var s in _extraStages) pipeline.AddStage(s);

        pipeline.Execute(map);

        var world = new World(map, _seed) { Planet = planet };
        world.Attach(pipeline, hydrologyStage?.MaxRivers ?? new HydrologyStage().MaxRivers, riverScale);
        world.PopulateFeatureCatalogs();
        world.RebuildIndex();
        return world;
    }
}
