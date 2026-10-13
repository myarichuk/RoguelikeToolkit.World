using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hierarchy;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Integration;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Queries;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Sites;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Public library facade over <see cref="WorldMap"/> plus managed feature
/// catalogs. Owns generation (via <see cref="WorldBuilder"/>) and all
/// game-facing queries. History is consumed here via <see cref="QueryOptions"/>.
/// </summary>
public sealed class World : IDisposable
{
    /// <summary>Planet radius in kilometres used by every distance and radius in the library. It is fixed and independent of <see cref="PlanetParameters"/>.</summary>
    public const double EarthRadiusKm = 6371.0;

    /// <summary>The tile store and layer views holding all per-tile generated data.</summary>
    public WorldMap Map { get; }
    /// <summary>The seed the world was built with; with the build configuration it fully determines the generated world.</summary>
    public int Seed { get; }
    /// <summary>Subdivision level of the planet's tile grid; see <see cref="WorldDataStore.Size"/>.</summary>
    public int Size => Map.DataStore.Size;
    /// <summary>Number of planet tiles, <c>10 * 4^Size + 2</c>.</summary>
    public int TileCount => Map.DataStore.TileCount;
    /// <summary>Rivers found by hydrology; filled at build time and refilled in place by <see cref="Recompute"/>.</summary>
    public RiverCatalog Rivers { get; } = new();
    /// <summary>Oceans and lakes found by hydrology; refilled in place by <see cref="Recompute"/>.</summary>
    public WaterBodyCatalog WaterBodies { get; } = new();
    /// <summary>Mountain ranges; refilled in place by <see cref="Recompute"/>.</summary>
    public RangeCatalog Ranges { get; } = new();
    /// <summary>Resource deposits, derived from the world seed and terrain; refilled in place by <see cref="Recompute"/>.</summary>
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
        _pipeline!.ExecuteFrom(Map, StageOrder.Elevation);
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

    /// <summary>Surface height of the tile nearest to <paramref name="coord"/>, in the dimensionless <see cref="Elevation.ElevationInfo.Height"/> scale (below <see cref="Elevation.ElevationGenerationStage.SeaLevel"/> is ocean). Throws <see cref="ArgumentOutOfRangeException"/> for a non-finite coordinate.</summary>
    public float SampleElevation(GeoCoord coord)
    {
        int i = Map.DataStore.GetTileIndex(coord);
        return Map.DataStore.GetRef<ElevationInfo>(i).Height;
    }

    /// <summary>Biome and danger level of the tile nearest to <paramref name="coord"/>. With <paramref name="options"/>, history modifiers adjust the danger and materialized overrides replace the biome and/or danger; the returned danger is never negative.</summary>
    public (BiomeType Biome, int Danger) SampleBiome(GeoCoord coord, QueryOptions? options = null)
    {
        int i = Map.DataStore.GetTileIndex(coord);
        var info = Map.DataStore.GetRef<BiomeInfo>(i);
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

    /// <summary>Great-circle distance in kilometres between two coordinates, on a sphere of radius <see cref="EarthRadiusKm"/> (at most half its circumference). Deterministic across platforms; derived from the angle's cosine, so very small distances are coarse.</summary>
    public static double DistanceKm(GeoCoord a, GeoCoord b)
    {
        var va = Vector3D.FromGeoCoord(a);
        var vb = Vector3D.FromGeoCoord(b);
        double dot = Math.Clamp(Vector3D.Dot(va, vb), -1.0, 1.0);
        return DetMath.Acos(dot) * EarthRadiusKm;
    }

    /// <summary>All tiles within radiusKm of center, sorted by distance.</summary>
    /// <param name="center">Query centre.</param>
    /// <param name="radiusKm">Great-circle radius in kilometres.</param>
    /// <param name="options">Currently ignored: history and materialized overrides are <b>not</b> applied to <see cref="TileHit.Biome"/> here (unlike <c>SampleBiome</c> and <c>ScoreCitySites</c>).</param>
    public List<TileHit> QueryRadius(GeoCoord center, double radiusKm, QueryOptions? options = null)
    {
        var store = Map.DataStore;
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        bool hasLocals = store.IsLayerRegistered<BiomeInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        var locals = hasLocals ? store.GetSpan<BiomeInfo>() : default;

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

    /// <summary>The region map of one planet tile, derived on demand (not cached) from the tile's data and the world seed. Throws <see cref="IndexOutOfRangeException"/> for a tile index outside <c>0..TileCount-1</c>.</summary>
    /// <param name="worldTileIndex">Planet tile to expand.</param>
    /// <param name="regionSize">Side of the square region patch, in cells.</param>
    public RegionHandle GetRegion(int worldTileIndex, int regionSize = RegionMaps.DefaultRegionSize)
        => GetRegion(new PlanetHex(Seed, worldTileIndex), regionSize);

    /// <summary>As <see cref="GetRegion(int, int)"/> for an already-resolved hex. Throws <see cref="ArgumentException"/> when <paramref name="hex"/> was issued by a world with a different seed.</summary>
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

    /// <summary>Ranks candidate settlement tiles that pass <paramref name="filter"/> (default filter when null), best first. Ties are broken by ascending tile index, so the order is deterministic, and the list is cut to the filter's <c>TopN</c>.</summary>
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

    /// <summary>WKT <c>LINESTRING</c> (longitude latitude order) following the river's tile path. <paramref name="riverId"/> indexes <see cref="Rivers"/>; throws <see cref="ArgumentOutOfRangeException"/> for an unknown id.</summary>
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
    /// <summary>WKT <c>POINT</c> (longitude latitude order) at the centre of the deposit's tile. <paramref name="depositId"/> indexes the deposit list; throws <see cref="ArgumentOutOfRangeException"/> for an unknown id.</summary>
    public string DepositToWkt(int depositId)
    {
        if ((uint)depositId >= (uint)Deposits.Deposits.Count)
            throw new ArgumentOutOfRangeException(nameof(depositId), depositId, $"Unknown deposit id {depositId}.");
        return GeoWkt.DepositToWkt(Deposits.Deposits[depositId], Map.DataStore);
    }

    /// <summary>Disposes the generation pipeline and the map (and so the store). Spans, references and handles obtained from the world must not be used afterwards.</summary>
    public void Dispose()
    {
        _pipeline?.Dispose();
        Map.Dispose();
    }
}
