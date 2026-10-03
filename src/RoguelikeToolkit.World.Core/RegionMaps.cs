using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Deterministic lazy derivation for region/local maps. Pure functions of
/// (address, seed, parent context, bounds); no storage, reproducible on demand.
/// Both tiers are flat-top hex grids (axial patch with hex adjacency):
/// <list type="bullet">
/// <item>base surface interpolated from the parent neighborhood (plane through
/// the parent mean carrying the parent slope);</item>
/// <item>detail from <see cref="SphereNoise.RidgedFbm"/> through an anisotropic
/// domain warp of the <see cref="Vector3D"/> sample point around the parent
/// orogeny strike;</item>
/// <item>edge pinning blending outer cells toward parent-interpolated boundary
/// values so sibling maps stitch;</item>
/// <item>river threading from the parent flow entry side to the exit side when
/// the parent tile <see cref="ParentContext.IsRiver">is a river</see> or has
/// high <see cref="ParentContext.Flow">flow</see>, else local drainage routed
/// along the inherited aspect.</item>
/// </list>
/// </summary>
public static class RegionMaps
{
    public const int DefaultRegionSize = 8;
    public const int DefaultLocalSize = 16;

    /// <summary>Water depth at or above this marks a threaded river reach on a local map.</summary>
    public const float LocalRiverMark = 0.025f;

    private const double SeaLevel = 0.0;


    public static RegionHandle DeriveRegionMap(
        MapAddress address,
        ParentContext parent,
        GeoCoord center,
        double radiusKm,
        uint seed,
        int size = DefaultRegionSize,
        RegionDetailOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (radiusKm < 0) throw new ArgumentOutOfRangeException(nameof(radiusKm));
        options ??= RegionDetailOptions.RegionDefault;

        var grid = ChildGridBuilder.Build(parent, center, radiusKm, seed, size, options, unchecked((int)(seed ^ 0x5245474E)));
        var cells = new RegionCell[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            float moisture = Math.Clamp(parent.MeanMoisture + (float)grid.Jitter[i] * 0.08f, 0f, 1f);
            cells[i] = new RegionCell
            {
                CellIndex = i,
                Seed = unchecked(seed + (uint)i * 2654435761u),
                Elevation = grid.Heights[i],
                Biome = RegionBiome(grid.Heights[i], parent.DominantBiome),
                Moisture = moisture,
                IsRiver = grid.IsRiver[i],
                WaterDepth = grid.WaterDepth[i],
                Flow = grid.Flow[i],
            };
        }

        return new RegionHandle(
            address.WorldTileIndex, seed, size, cells,
            address, MapBounds.ForGrid(center, radiusKm, size), parent,
            grid.RiverEntry, grid.RiverExit);
    }

    public static LocalMapHandle DeriveLocalMap(
        MapAddress address,
        ParentContext parent,
        GeoCoord center,
        double radiusKm,
        uint seed,
        int size = DefaultLocalSize,
        RegionDetailOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (radiusKm < 0) throw new ArgumentOutOfRangeException(nameof(radiusKm));
        options ??= RegionDetailOptions.LocalDefault;

        var grid = ChildGridBuilder.Build(parent, center, radiusKm, seed, size, options, unchecked((int)(seed ^ 0x4C4F4341)));
        var tiles = new LocalTile[grid.Count];
        for (int i = 0; i < grid.Count; i++)
        {
            float h = grid.Heights[i];
            float temp = Math.Clamp(parent.MeanTemperature - Math.Max(0f, h) * 0.35f + (float)grid.Jitter[i] * 0.05f, 0f, 1f);
            float precip = Math.Clamp(parent.MeanPrecipitation + (float)grid.Jitter[i] * 0.08f, 0f, 1f);
            bool water = h < SeaLevel;
            var (biome, danger) = LocalBiome(h, temp, precip);
            if (grid.IsRiver[i]) danger = Math.Max(danger, (byte)1);
            tiles[i] = new LocalTile
            {
                Index = i,
                Height = h,
                Biome = biome,
                Danger = danger,
                IsWater = water,
                Temperature = temp,
                Precipitation = precip,
                WaterDepth = grid.WaterDepth[i],
                ParentBiome = parent.DominantBiome,
            };
        }

        return new LocalMapHandle(
            address.WorldTileIndex, address.RegionCellIndex, seed, size, tiles,
            address, MapBounds.ForGrid(center, radiusKm, size), parent,
            grid.RiverEntry, grid.RiverExit);
    }

    /// <summary>
    /// Builds the child <see cref="ParentContext"/> for one region cell from its
    /// neighborhood: slope/height from a local plane fit, strike inherited from
    /// the region parent (the local tier couples to it loosely via
    /// <see cref="RegionDetailOptions.LocalDefault"/>), climate means carried
    /// down, river state from the cell.
    /// </summary>
    public static ParentContext DeriveChildContext(RegionHandle region, int cellIndex)
    {
        ArgumentNullException.ThrowIfNull(region);
        if ((uint)cellIndex >= (uint)region.Cells.Length) throw new IndexOutOfRangeException();
        return ChildContextDeriver.Derive(region, cellIndex);
    }

    /// <summary>Derives the local map for one cell of a region map.</summary>
    public static LocalMapHandle DeriveLocalMapForCell(RegionHandle region, int cellIndex, int localSize = DefaultLocalSize)
    {
        ArgumentNullException.ThrowIfNull(region);
        if ((uint)cellIndex >= (uint)region.Cells.Length) throw new IndexOutOfRangeException();

        var child = ChildContextDeriver.Derive(region, cellIndex);
        var (cq, cr) = OffsetGrid.FromIndex(cellIndex, region.Size);
        var (u, v) = CellUV(cq, cr, region.Size);
        var childCenter = new TangentFrame(region.Bounds.Center, region.Bounds.RadiusKm, 1e-9).Offset(u, v).ToGeoCoord();
        double childRadiusKm = region.Bounds.RadiusKm / region.Size;

        int worldSeed = region.Address.WorldSeed;
        uint seed = MapSeeds.DeriveLocalSeed(worldSeed, region.WorldTileIndex, cellIndex);
        var address = MapAddress.ForLocal(worldSeed, region.WorldTileIndex, cellIndex, -1);
        return DeriveLocalMap(address, child, childCenter, childRadiusKm, seed, localSize);
    }

    /// <summary>
    /// Parent-interpolated base elevation at normalized coords (u, v in [-1, 1]):
    /// the parent mean plus the parent slope. Edge pinning blends outer cells
    /// toward exactly this value, so sibling maps stitch within the detail
    /// amplitude at worst.
    /// </summary>
    public static double ParentBaseElevation(ParentContext parent, GeoCoord center, double radiusKm, double u, double v)
    {
        ArgumentNullException.ThrowIfNull(parent);
        var frame = new TangentFrame(center, radiusKm, 0.0);
        double gU = Vector3D.Dot(parent.ElevationGradient, frame.East);
        double gV = Vector3D.Dot(parent.ElevationGradient, frame.North);
        return parent.MeanElevation + (gU * u + gV * v) * frame.Angle;
    }

    // ------------------------------------------------------------------ grid geometry (see GridProjection)

    /// <summary>Normalized flat-top hex coords (u, v in [-1, 1]) for axial (q, r).</summary>
    public static (double U, double V) CellUV(int q, int r, int size) => GridProjection.CellUV(q, r, size);

    /// <summary>
    /// Geographic center of cell <paramref name="index"/> on a size x size grid
    /// centered at <paramref name="mapCenter"/> with bounding radius
    /// <paramref name="radiusKm"/>: the <see cref="CellUV"/> offset projected
    /// onto the tangent plane and normalized back to the sphere. This is the
    /// same projection <see cref="DeriveChildContext"/> and
    /// <see cref="DeriveLocalMapForCell"/> use, so handle coordinates agree
    /// with the grid that generated the cells (pinned by round-trip tests).
    /// </summary>
    public static GeoCoord GridCellCenter(GeoCoord mapCenter, double radiusKm, int size, int index)
        => GridProjection.GridCellCenter(mapCenter, radiusKm, size, index);

    /// <summary>
    /// Index of the grid cell containing <paramref name="coord"/>: the nearest
    /// cell center on the sphere (lowest index wins ties, so results are
    /// deterministic). Pure function of the bounds; no store access.
    /// </summary>
    public static int GridCellAt(GeoCoord mapCenter, double radiusKm, int size, GeoCoord coord)
        => GridProjection.GridCellAt(mapCenter, radiusKm, size, coord);

    /// <summary>Exhaustive nearest-center scan; the verification path for <see cref="GridCellAt"/>.</summary>
    public static int GridCellAtExact(GeoCoord mapCenter, double radiusKm, int size, GeoCoord coord)
        => GridProjection.GridCellAtExact(mapCenter, radiusKm, size, coord);

    /// <summary>Hex neighbors of a cell on a size x size axial patch (clipped to the patch).</summary>
    public static int GetHexAdjacent(int index, int size, Span<int> neighbors)
        => GridProjection.GetHexAdjacent(index, size, neighbors);

    internal static GeoCoord OffsetToGeo(GeoCoord center, double radiusKm, double u, double v)
        => GridProjection.OffsetToGeo(center, radiusKm, u, v);

    // ------------------------------------------------------------------ obsolete shims

    [Obsolete("Use DeriveRegionMap with an explicit ParentContext, center, and bounds. This shim derives a flat default parent.")]
    public static RegionHandle DeriveRegion(int worldSeed, int worldTileIndex, float baseElevation, BiomeType baseBiome, float baseMoisture, int size = DefaultRegionSize)
    {
        var parent = new ParentContext
        {
            MeanElevation = baseElevation,
            DominantBiome = baseBiome,
            MeanMoisture = baseMoisture,
        };
        var center = new GeoCoord(0, 0);
        const double radiusKm = 120.0;
        uint seed = Rng.DeriveTileSeed(worldSeed, worldTileIndex);
        return DeriveRegionMap(MapAddress.ForRegion(worldSeed, worldTileIndex, -1), parent, center, radiusKm, seed, size);
    }

    [Obsolete("Use DeriveLocalMap with an explicit ParentContext, center, and bounds. This shim derives a flat default parent (world seed unknown, address uses 0).")]
    public static LocalMapHandle DeriveLocal(int worldTileIndex, int regionCellIndex, uint regionCellSeed, int size = DefaultLocalSize)
    {
        var parent = new ParentContext();
        var center = new GeoCoord(0, 0);
        const double radiusKm = 15.0;
        return DeriveLocalMap(
            MapAddress.ForLocal(0, worldTileIndex, regionCellIndex, -1),
            parent, center, radiusKm, regionCellSeed, size);
    }

    // ------------------------------------------------------------------ classification

    private static BiomeType RegionBiome(float elevation, BiomeType parentBiome)
    {
        if (elevation < SeaLevel) return BiomeType.Ocean;
        if (elevation > 0.55f) return BiomeType.Mountain;
        return parentBiome == BiomeType.Ocean ? BiomeType.Plains : parentBiome;
    }

    private static (BiomeType Biome, byte Danger) LocalBiome(float height, float temperature, float moisture)
        => BiomeClassifier.Classify(height, temperature, moisture);
}
