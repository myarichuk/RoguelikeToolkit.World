using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Lazy hierarchical detail: one world hex expands to a region grid.</summary>
public sealed class RegionHandle : IHexMap<RegionCell>
{
    public int WorldTileIndex { get; }
    public uint Seed { get; }
    public int Size { get; }
    public RegionCell[] Cells { get; }
    public MapAddress Address { get; }
    public MapBounds Bounds { get; }
    public ParentContext Parent { get; }
    /// <summary>Entry border cell of the threaded river, or -1 when there is none.</summary>
    public int RiverEntryCell { get; }
    /// <summary>Exit border cell of the threaded river, or -1 when there is none.</summary>
    public int RiverExitCell { get; }
    /// <summary>Whether a parent river was threaded through this map.</summary>
    public bool HasRiver => RiverEntryCell >= 0;

    internal RegionHandle(
        int worldTileIndex, uint seed, int size, RegionCell[] cells,
        MapAddress address, MapBounds bounds, ParentContext parent,
        int riverEntryCell, int riverExitCell)
    {
        WorldTileIndex = worldTileIndex;
        Seed = seed;
        Size = size;
        Cells = cells;
        Address = address;
        Bounds = bounds;
        Parent = parent;
        RiverEntryCell = riverEntryCell;
        RiverExitCell = riverExitCell;
    }

    public int TileCount => Size * Size;
    public MapAddress RootAddress => Address;
    public RegionCell GetTile(int index) => Cells[index];
    public int GetAdjacent(int index, Span<int> neighbors) => RegionMaps.GetHexAdjacent(index, Size, neighbors);
    public int GetTileAt(int q, int r) => OffsetGrid.ToIndex(q, r, Size);
    public int GetTileAt(CubeCoord cube) => !cube.IsValid ? -1 : GetTileAt(cube.ToAxial().Q, cube.ToAxial().R);

    /// <summary>Geographic center of one region cell: the CellUV offset projected onto the tangent plane at Bounds.Center and normalized back to the sphere — the same projection derivation uses.</summary>
    public GeoCoord CellCenter(int cellIndex)
        => RegionMaps.GridCellCenter(Bounds.Center, Bounds.RadiusKm, Size, cellIndex);

    /// <summary>Index of the cell containing a coordinate (nearest center; deterministic).</summary>
    public int CellAt(GeoCoord coord)
        => RegionMaps.GridCellAt(Bounds.Center, Bounds.RadiusKm, Size, coord);

    public LocalMapHandle GetLocal(int cellIndex, int localSize = RegionMaps.DefaultLocalSize)
    {
        if ((uint)cellIndex >= (uint)Cells.Length) throw new IndexOutOfRangeException();
        return RegionMaps.DeriveLocalMapForCell(this, cellIndex, localSize);
    }

    /// <summary>
    /// Materializes injector sites for this region map. Injectors run ascending
    /// by <see cref="ISiteInjector.Order"/>; duplicate Order values throw
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public SiteCatalog WithInjectors(IEnumerable<ISiteInjector> injectors)
        => SiteInjectorPipeline.MaterializeRegion(this, injectors);
}

public struct RegionCell
{
    public int CellIndex;
    public uint Seed;
    public float Elevation;
    public BiomeType Biome;
    public float Moisture;
    public bool IsRiver;
    public float WaterDepth;
    /// <summary>Local discharge in cell-count units (1 per cell, routed downhill).</summary>
    public float Flow;
}

/// <summary>Leaf detail: one region cell expands to a local tile grid.</summary>
public sealed class LocalMapHandle : IHexMap<LocalTile>
{
    public int WorldTileIndex { get; }
    public int RegionCellIndex { get; }
    public uint Seed { get; }
    public int Size { get; }
    public LocalTile[] Tiles { get; }
    public MapAddress Address { get; }
    public MapBounds Bounds { get; }
    public ParentContext Parent { get; }
    /// <summary>Entry border tile of the threaded river, or -1 when there is none.</summary>
    public int RiverEntryCell { get; }
    /// <summary>Exit border tile of the threaded river, or -1 when there is none.</summary>
    public int RiverExitCell { get; }
    /// <summary>Whether a parent river was threaded through this map.</summary>
    public bool HasRiver => RiverEntryCell >= 0;

    internal LocalMapHandle(
        int worldTileIndex, int regionCellIndex, uint seed, int size, LocalTile[] tiles,
        MapAddress address, MapBounds bounds, ParentContext parent,
        int riverEntryCell, int riverExitCell)
    {
        WorldTileIndex = worldTileIndex;
        RegionCellIndex = regionCellIndex;
        Seed = seed;
        Size = size;
        Tiles = tiles;
        Address = address;
        Bounds = bounds;
        Parent = parent;
        RiverEntryCell = riverEntryCell;
        RiverExitCell = riverExitCell;
    }

    /// <summary>
    /// Materializes injector sites for this local map. Injectors run ascending
    /// by <see cref="ISiteInjector.Order"/>; duplicate Order values throw
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public SiteCatalog WithInjectors(IEnumerable<ISiteInjector> injectors)
        => SiteInjectorPipeline.MaterializeLocal(this, injectors);

    public int TileCount => Size * Size;
    public MapAddress RootAddress => Address;
    public LocalTile GetTile(int index) => Tiles[index];

    /// <summary>Geographic center of one local tile: the CellUV offset projected onto the tangent plane at Bounds.Center and normalized back to the sphere — the same projection derivation uses.</summary>
    public GeoCoord TileCenter(int tileIndex)
        => RegionMaps.GridCellCenter(Bounds.Center, Bounds.RadiusKm, Size, tileIndex);

    /// <summary>Index of the tile containing a coordinate (nearest center; deterministic).</summary>
    public int TileAt(GeoCoord coord)
        => RegionMaps.GridCellAt(Bounds.Center, Bounds.RadiusKm, Size, coord);
    public int GetAdjacent(int index, Span<int> neighbors) => RegionMaps.GetHexAdjacent(index, Size, neighbors);
    public int GetTileAt(int q, int r) => OffsetGrid.ToIndex(q, r, Size);
    public int GetTileAt(CubeCoord cube) => !cube.IsValid ? -1 : GetTileAt(cube.ToAxial().Q, cube.ToAxial().R);
}

public struct LocalTile
{
    public int Index;
    public float Height;
    public BiomeType Biome;
    public byte Danger;
    public bool IsWater;
    public float Temperature;
    public float Precipitation;
    public float WaterDepth;
    public BiomeType ParentBiome;
}

/// <summary>
/// Detail knobs for one child-map tier. The warp-strength parameters
/// (<see cref="AcrossStrikeScale"/>, <see cref="AlongStrikeScale"/>) deform the
/// noise domain around the parent orogeny strike: coordinates across the strike
/// are compressed (higher ridge frequency) while coordinates along it are
/// elongated (smooth ridge crests). The region tier follows the parent strike
/// tightly; the local tier uses looser defaults.
/// </summary>
public sealed class RegionDetailOptions
{
    /// <summary>Lattice units spanned by the map half-extent before warping.</summary>
    public double DetailFrequency { get; set; } = 1.0;
    /// <summary>Across-strike coordinate multiplier (&gt;1 compresses: tighter ridges).</summary>
    public double AcrossStrikeScale { get; set; } = 3.0;
    /// <summary>Along-strike coordinate multiplier (&lt;1 elongates: smoother crests).</summary>
    public double AlongStrikeScale { get; set; } = 0.2;
    /// <summary>Peak detail height added (or removed) in the map interior.</summary>
    public float DetailAmplitude { get; set; } = 0.12f;
    /// <summary>Edge-pinning blend width in cells (outer ring is fully pinned).</summary>
    public double EdgePinCells { get; set; } = 2.0;
    /// <summary>Depth carved along a threaded river reach (tapered at both ends).</summary>
    public float RiverCarveDepth { get; set; } = 0.08f;
    /// <summary>Parent <see cref="ParentContext.Flow"/> at or above this threads a river.</summary>
    public float RiverFlowThreshold { get; set; } = 6f;
    public int NoiseOctaves { get; set; } = 3;
    /// <summary>Ridged-Fbm octave gain (&lt;0.5 emphasizes large strike-parallel structure).</summary>
    public double DetailGain { get; set; } = 0.5;

    /// <summary>Tight strike coupling for the region tier.</summary>
    public static RegionDetailOptions RegionDefault => new()
    {
        DetailFrequency = 1.0,
        AcrossStrikeScale = 3.0,
        AlongStrikeScale = 0.2,
        DetailAmplitude = 0.12f,
        EdgePinCells = 2.0,
        RiverCarveDepth = 0.08f,
        RiverFlowThreshold = 6f,
        NoiseOctaves = 3,
        DetailGain = 0.5,
    };

    /// <summary>Looser strike coupling for the local tier.</summary>
    public static RegionDetailOptions LocalDefault => new()
    {
        DetailFrequency = 1.0,
        AcrossStrikeScale = 1.8,
        AlongStrikeScale = 0.5,
        DetailAmplitude = 0.15f,
        EdgePinCells = 2.0,
        RiverCarveDepth = 0.09f,
        RiverFlowThreshold = 6f,
        NoiseOctaves = 3,
        DetailGain = 0.5,
    };
}

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

    // ------------------------------------------------------------------ entry points

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

        var grid = BuildGrid(parent, center, radiusKm, seed, size, options, unchecked((int)(seed ^ 0x5245474E)));
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

        var grid = BuildGrid(parent, center, radiusKm, seed, size, options, unchecked((int)(seed ^ 0x4C4F4341)));
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

        var parent = region.Parent;
        int size = region.Size;
        var (cq, cr) = OffsetGrid.FromIndex(cellIndex, size);

        Span<int> nb = stackalloc int[6];
        int adjacent = GetHexAdjacent(cellIndex, size, nb);

        var center = region.Bounds.Center;
        var up = Vector3D.FromGeoCoord(center);
        var (east, north) = EastNorth(center, up);
        double ang = Math.Max(region.Bounds.RadiusKm / World.EarthRadiusKm, 1e-9);

        // Least-squares plane fit over the cell + hex ring, in radians.
        double sEE = 0, sEN = 0, sNN = 0, sE = 0, sN = 0;
        double sumH = region.Cells[cellIndex].Elevation;
        int count = 1;
        var ringHeights = new float[adjacent];
        var (ux0, vx0) = CellUV(cq, cr, size);
        var pts = new (double E, double N, double H)[adjacent + 1];
        pts[0] = (ux0 * ang, vx0 * ang, region.Cells[cellIndex].Elevation);
        for (int k = 0; k < adjacent; k++)
        {
            var (nq, nr) = OffsetGrid.FromIndex(nb[k], size);
            var (nu, nv) = CellUV(nq, nr, size);
            float h = region.Cells[nb[k]].Elevation;
            pts[k + 1] = (nu * ang, nv * ang, h);
            ringHeights[k] = h;
            sumH += h;
            count++;
        }
        double mean = sumH / count;
        for (int k = 0; k < pts.Length; k++)
        {
            double dh = pts[k].H - mean;
            sEE += pts[k].E * pts[k].E; sEN += pts[k].E * pts[k].N; sNN += pts[k].N * pts[k].N;
            sE += dh * pts[k].E; sN += dh * pts[k].N;
        }
        double det = sEE * sNN - sEN * sEN;
        double gE, gN;
        if (Math.Abs(det) > 1e-12)
        {
            gE = (sE * sNN - sN * sEN) / det;
            gN = (sEE * sN - sEN * sE) / det;
        }
        else
        {
            gE = Vector3D.Dot(parent.ElevationGradient, east);
            gN = Vector3D.Dot(parent.ElevationGradient, north);
        }
        var gradient = east * gE + north * gN;

        // Child center: offset from the region center by the cell position.
        var childPos = (up + east * (ux0 * ang) + north * (vx0 * ang)).Normalize();
        var childCenter = childPos.ToGeoCoord();
        var upC = childPos;
        var (eastC, northC) = EastNorth(childCenter, upC);

        // Strike inherited from the region parent, re-projected at the child center.
        var strike = parent.OrogenyStrike - upC * Vector3D.Dot(parent.OrogenyStrike, upC);
        strike = strike.Length > 1e-9 ? strike.Normalize() : eastC;

        double aspect = 0;
        if (gradient.Length > 1e-9)
        {
            var downhill = gradient * -1;
            aspect = Math.Atan2(Vector3D.Dot(downhill, eastC), Vector3D.Dot(downhill, northC));
        }

        var cell = region.Cells[cellIndex];
        int entry = -1, exit = -1;
        float maxH = float.NegativeInfinity, minH = float.PositiveInfinity;
        for (int k = 0; k < adjacent; k++)
        {
            if (ringHeights[k] > maxH) { maxH = ringHeights[k]; entry = nb[k]; }
            if (ringHeights[k] < minH) { minH = ringHeights[k]; exit = nb[k]; }
        }
        if (adjacent == 0) entry = -1;
        if (adjacent == 0 || minH >= cell.Elevation) exit = -1;

        return new ParentContext
        {
            MeanElevation = (float)mean,
            DominantBiome = cell.Biome,
            MeanTemperature = parent.MeanTemperature,
            MeanPrecipitation = parent.MeanPrecipitation,
            MeanMoisture = cell.Moisture,
            ElevationGradient = gradient,
            AspectRadians = aspect,
            OrogenyStrike = strike,
            NeighborHeights = ringHeights,
            FlowEntryTile = entry,
            FlowExitTile = exit,
            Wind = parent.Wind,
            IsRiver = cell.IsRiver,
            Flow = cell.Flow,
        };
    }

    /// <summary>Derives the local map for one cell of a region map.</summary>
    public static LocalMapHandle DeriveLocalMapForCell(RegionHandle region, int cellIndex, int localSize = DefaultLocalSize)
    {
        ArgumentNullException.ThrowIfNull(region);
        if ((uint)cellIndex >= (uint)region.Cells.Length) throw new IndexOutOfRangeException();

        var child = DeriveChildContext(region, cellIndex);
        int size = region.Size;
        var (cq, cr) = OffsetGrid.FromIndex(cellIndex, size);
        var (u, v) = CellUV(cq, cr, size);
        double ang = Math.Max(region.Bounds.RadiusKm / World.EarthRadiusKm, 1e-9);

        var center = region.Bounds.Center;
        var up = Vector3D.FromGeoCoord(center);
        var (east, north) = EastNorth(center, up);
        var childCenter = (up + east * (u * ang) + north * (v * ang)).Normalize().ToGeoCoord();
        double childRadiusKm = region.Bounds.RadiusKm / size;

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
        var up = Vector3D.FromGeoCoord(center);
        var (east, north) = EastNorth(center, up);
        double ang = Math.Max(radiusKm / World.EarthRadiusKm, 0.0);
        double gU = Vector3D.Dot(parent.ElevationGradient, east);
        double gV = Vector3D.Dot(parent.ElevationGradient, north);
        return parent.MeanElevation + (gU * u + gV * v) * ang;
    }

    /// <summary>Normalized flat-top hex coords (u, v in [-1, 1]) for axial (q, r).</summary>
    public static (double U, double V) CellUV(int q, int r, int size)
    {
        if (size <= 1) return (0.0, 0.0);
        double x = 1.5 * q;
        double y = Sqrt3 * (r + q * 0.5);
        double half = 0.75 * (size - 1);
        double cx = half;
        double cy = Sqrt3 * half;
        return ((x - cx) / half, (y - cy) / (Sqrt3 * half));
    }

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
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if ((uint)index >= (uint)(size * size)) throw new IndexOutOfRangeException();
        var (q, r) = OffsetGrid.FromIndex(index, size);
        var (u, v) = CellUV(q, r, size);
        return OffsetToGeo(mapCenter, radiusKm, u, v);
    }

    /// <summary>
    /// Index of the grid cell containing <paramref name="coord"/>: the nearest
    /// cell center on the sphere (lowest index wins ties, so results are
    /// deterministic). Pure function of the bounds; no store access.
    /// </summary>
    public static int GridCellAt(GeoCoord mapCenter, double radiusKm, int size, GeoCoord coord)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        var target = Vector3D.FromGeoCoord(coord);

        // Fast path: invert the gnomonic projection analytically to get the
        // fractional (q, r), then take the exact nearest of a small window
        // around it. The window is exact for realistic region spans; wide maps
        // (strong projection distortion), far-off/back-hemisphere targets and
        // tiny grids take the exhaustive scan.
        double ang = Math.Max(radiusKm / World.EarthRadiusKm, 1e-9);
        if (size > 3 && ang < 0.35)
        {
            var up = Vector3D.FromGeoCoord(mapCenter);
            var (east, north) = EastNorth(mapCenter, up);
            double depth = Vector3D.Dot(target, up);
            if (depth > 0.5)
            {
                double u = Vector3D.Dot(target, east) / depth / ang;
                double v = Vector3D.Dot(target, north) / depth / ang;
                double half = 0.75 * (size - 1);
                double qf = (u * half + half) / 1.5;
                double rf = half * (v + 1.0) - qf * 0.5;
                if (qf > -2 && qf < size + 1 && rf > -2 && rf < size + 1)
                {
                    int q0 = (int)Math.Floor(qf), r0 = (int)Math.Floor(rf);
                    int best = -1;
                    double bestDot = double.NegativeInfinity;
                    // Row-major ascending so ties resolve to the lowest index, as in the full scan.
                    for (int r = Math.Max(0, r0 - 2); r <= Math.Min(size - 1, r0 + 3); r++)
                        for (int q = Math.Max(0, q0 - 2); q <= Math.Min(size - 1, q0 + 3); q++)
                        {
                            int i = r * size + q;
                            double dot = Vector3D.Dot(target, Vector3D.FromGeoCoord(GridCellCenter(mapCenter, radiusKm, size, i)));
                            if (dot > bestDot) { bestDot = dot; best = i; }
                        }
                    if (best >= 0) return best;
                }
            }
        }
        return GridCellAtExact(mapCenter, radiusKm, size, coord);
    }

    /// <summary>Exhaustive nearest-center scan; the verification path for <see cref="GridCellAt"/>.</summary>
    public static int GridCellAtExact(GeoCoord mapCenter, double radiusKm, int size, GeoCoord coord)
    {
        var target = Vector3D.FromGeoCoord(coord);
        int best = 0;
        double bestDot = double.NegativeInfinity;
        for (int i = 0; i < size * size; i++)
        {
            double dot = Vector3D.Dot(target, Vector3D.FromGeoCoord(GridCellCenter(mapCenter, radiusKm, size, i)));
            if (dot > bestDot) { bestDot = dot; best = i; }
        }
        return best;
    }

    internal static GeoCoord OffsetToGeo(GeoCoord center, double radiusKm, double u, double v)
    {
        var up = Vector3D.FromGeoCoord(center);
        var (east, north) = EastNorth(center, up);
        double ang = Math.Max(radiusKm / World.EarthRadiusKm, 1e-9);
        return (up + east * (u * ang) + north * (v * ang)).Normalize().ToGeoCoord();
    }

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

    // ------------------------------------------------------------------ grid internals

    private const double Sqrt3 = 1.7320508075688772;

    private sealed class ChildGrid
    {
        public int Count;
        public float[] Heights = Array.Empty<float>();
        public double[] Jitter = Array.Empty<double>();
        public bool[] IsRiver = Array.Empty<bool>();
        public float[] WaterDepth = Array.Empty<float>();
        public float[] Flow = Array.Empty<float>();
        public int RiverEntry = -1;
        public int RiverExit = -1;
    }

    /// <summary>Hex neighbors of a cell on a size x size axial patch (clipped to the patch).</summary>
    public static int GetHexAdjacent(int index, int size, Span<int> neighbors)
    {
        var (q, r) = OffsetGrid.FromIndex(index, size);
        int count = 0;
        // Axial hex neighborhood on the square patch.
        if (q + 1 < size && count < neighbors.Length) neighbors[count++] = index + 1;
        if (q - 1 >= 0 && count < neighbors.Length) neighbors[count++] = index - 1;
        if (r + 1 < size && count < neighbors.Length) neighbors[count++] = index + size;
        if (r - 1 >= 0 && count < neighbors.Length) neighbors[count++] = index - size;
        if (q + 1 < size && r - 1 >= 0 && count < neighbors.Length) neighbors[count++] = index + 1 - size;
        if (q - 1 >= 0 && r + 1 < size && count < neighbors.Length) neighbors[count++] = index - 1 + size;
        return count;
    }

    private static (Vector3D East, Vector3D North) EastNorth(GeoCoord center, Vector3D up)
    {
        double lonR = center.Longitude * GeoCoord.Deg2Rad;
        var east = new Vector3D(-Math.Sin(lonR), Math.Cos(lonR), 0);
        if (east.Length < 1e-9) east = new Vector3D(1, 0, 0);
        east = east.Normalize();
        var north = Vector3D.Cross(up, east).Normalize();
        return (east, north);
    }

    private static ChildGrid BuildGrid(
        ParentContext parent, GeoCoord center, double radiusKm,
        uint seed, int size, RegionDetailOptions options, int noiseSeed)
    {
        int count = size * size;
        var grid = new ChildGrid
        {
            Count = count,
            Heights = new float[count],
            Jitter = new double[count],
            IsRiver = new bool[count],
            WaterDepth = new float[count],
            Flow = new float[count],
        };

        var up = Vector3D.FromGeoCoord(center);
        var (east, north) = EastNorth(center, up);
        double ang = Math.Max(radiusKm / World.EarthRadiusKm, 0.0);

        // Strike in the local east/north frame (unit, with contour fallback).
        double sU = Vector3D.Dot(parent.OrogenyStrike, east);
        double sV = Vector3D.Dot(parent.OrogenyStrike, north);
        double sLen = Math.Sqrt(sU * sU + sV * sV);
        if (sLen < 1e-9) { sU = 1; sV = 0; sLen = 1; }
        sU /= sLen; sV /= sLen;
        var strike = east * sU + north * sV;
        var perp = east * -sV + north * sU;

        double gU = Vector3D.Dot(parent.ElevationGradient, east);
        double gV = Vector3D.Dot(parent.ElevationGradient, north);

        double across = options.AcrossStrikeScale;
        double along = options.AlongStrikeScale;
        double freq = options.DetailFrequency;
        float amp = options.DetailAmplitude;
        double pinWidth = Math.Max(options.EdgePinCells, 1e-6);
        double kBase = 5.25 + (seed % 4096) * 0.00213;

        var xs = new double[count];
        var ys = new double[count];
        var isEdge = new bool[count];
        for (int i = 0; i < count; i++)
        {
            var (q, r) = OffsetGrid.FromIndex(i, size);
            var (u, v) = CellUV(q, r, size);
            xs[i] = 1.5 * q;
            ys[i] = Sqrt3 * (r + q * 0.5);
            isEdge[i] = q == 0 || r == 0 || q == size - 1 || r == size - 1;

            double baseH = parent.MeanElevation + (gU * u + gV * v) * ang;

            // Anisotropic warp: compress across the strike, elongate along it.
            double s = (u * sU + v * sV) * freq;
            double c = (-u * sV + v * sU) * freq;
            var sample = up * kBase + strike * (s * along) + perp * (c * across);
            double detail = SphereNoise.RidgedFbm(sample, noiseSeed, options.NoiseOctaves, 2.1, options.DetailGain);
            grid.Jitter[i] = SphereNoise.Value(sample * 0.5 + up * 3.7, noiseSeed + 77);

            int edge = Math.Min(Math.Min(q, size - 1 - q), Math.Min(r, size - 1 - r));
            double t = Math.Clamp(edge / pinWidth, 0.0, 1.0);
            double pin = t * t * (3.0 - 2.0 * t);
            grid.Heights[i] = (float)(baseH + ((detail - 0.52) * 2.0) * amp * pin);
        }

        // River threading (parent river or high parent flow) else aspect drainage.
        bool wantRiver = parent.IsRiver || parent.Flow >= options.RiverFlowThreshold;
        double downhillX = Math.Sin(parent.AspectRadians);
        double downhillY = Math.Cos(parent.AspectRadians);

        if (wantRiver && count > 0)
        {
            int entry = -1, exit = -1;
            double minD = double.PositiveInfinity, maxD = double.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (!isEdge[i]) continue;
                double d = xs[i] * downhillX + ys[i] * downhillY;
                if (d < minD - 1e-12 || (Math.Abs(d - minD) <= 1e-12 && (entry < 0 || i < entry))) { minD = d; entry = i; }
                if (d > maxD + 1e-12 || (Math.Abs(d - maxD) <= 1e-12 && (exit < 0 || i < exit))) { maxD = d; exit = i; }
            }
            if (entry >= 0 && exit >= 0)
            {
                var path = FindRiverPath(grid.Heights, size, entry, exit);
                grid.RiverEntry = entry;
                grid.RiverExit = exit;
                for (int k = 0; k < path.Count; k++)
                {
                    int cell = path[k];
                    double profile = path.Count > 1
                        ? Math.Pow(Math.Sin(Math.PI * (k + 0.5) / path.Count), 0.75)
                        : 1.0;
                    float depth = (float)(options.RiverCarveDepth * profile);
                    grid.Heights[cell] -= depth;
                    grid.IsRiver[cell] = true;
                    grid.WaterDepth[cell] = Math.Max(grid.WaterDepth[cell], 0.03f + depth * 0.8f);
                }
            }
        }

        // Local discharge on the final surface; sinks spill along the aspect.
        var flow = AccumulateFlow(grid.Heights, size, xs, ys, downhillX, downhillY);
        for (int i = 0; i < count; i++)
        {
            grid.Flow[i] = flow[i];
            if (grid.Heights[i] < SeaLevel)
            {
                grid.WaterDepth[i] = Math.Max(grid.WaterDepth[i], (float)(SeaLevel - grid.Heights[i]));
            }
            else if (!grid.IsRiver[i] && flow[i] >= 5f)
            {
                grid.WaterDepth[i] = Math.Max(grid.WaterDepth[i], Math.Min(0.015f, 0.004f * (flow[i] - 4f)));
            }
        }

        return grid;
    }

    private static List<int> FindRiverPath(float[] heights, int size, int entry, int exit)
    {
        int n = heights.Length;
        var g = new double[n];
        var cameFrom = new int[n];
        var closed = new bool[n];
        for (int i = 0; i < n; i++) { g[i] = double.PositiveInfinity; cameFrom[i] = -1; }
        g[entry] = 0;
        var pq = new PriorityQueue<int, (double F, int Index)>();
        pq.Enqueue(entry, (HexDistance(entry, exit, size), entry));
        Span<int> nb = stackalloc int[6];
        while (pq.Count > 0)
        {
            int cur = pq.Dequeue();
            if (closed[cur]) continue;
            closed[cur] = true;
            if (cur == exit) break;
            int m = GetHexAdjacent(cur, size, nb);
            for (int k = 0; k < m; k++)
            {
                int j = nb[k];
                if (closed[j]) continue;
                double step = 1.0 + Math.Max(0.0, heights[j] - heights[cur]) * 12.0;
                double ng = g[cur] + step;
                if (ng < g[j] - 1e-12)
                {
                    g[j] = ng;
                    cameFrom[j] = cur;
                    pq.Enqueue(j, (ng + HexDistance(j, exit, size), j));
                }
            }
        }
        var path = new List<int>();
        if (!closed[exit]) { path.Add(entry); return path; }
        for (int c = exit; c >= 0; c = cameFrom[c])
        {
            path.Add(c);
            if (c == entry) break;
        }
        path.Reverse();
        return path;
    }

    private static int HexDistance(int a, int b, int size)
    {
        var (aq, ar) = OffsetGrid.FromIndex(a, size);
        var (bq, br) = OffsetGrid.FromIndex(b, size);
        int dq = aq - bq, dr = ar - br;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    private static float[] AccumulateFlow(
        float[] heights, int size, double[] xs, double[] ys, double downhillX, double downhillY)
    {
        int n = heights.Length;
        var flow = new float[n];
        for (int i = 0; i < n; i++) flow[i] = 1f;
        var order = TileOrdering.DescendingByValue(heights);
        Span<int> nb = stackalloc int[6];
        foreach (int i in order)
        {
            int m = GetHexAdjacent(i, size, nb);
            int best = -1;
            float bestH = heights[i];
            for (int k = 0; k < m; k++)
            {
                int j = nb[k];
                if (heights[j] < bestH || (best >= 0 && heights[j] == bestH && j < best))
                { bestH = heights[j]; best = j; }
            }
            if (best < 0 && m > 0)
            {
                // Flat/sink spill along the inherited aspect.
                double align = double.NegativeInfinity;
                for (int k = 0; k < m; k++)
                {
                    int j = nb[k];
                    var (jq, jr) = OffsetGrid.FromIndex(j, size);
                    var (iq, ir) = OffsetGrid.FromIndex(i, size);
                    double dx = 1.5 * (jq - iq);
                    double dy = Sqrt3 * ((jq - iq) * 0.5 + (jr - ir));
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    double a = len > 1e-12 ? (dx * downhillX + dy * downhillY) / len : -2.0;
                    if (a > align + 1e-12 || (Math.Abs(a - align) <= 1e-12 && (best < 0 || heights[j] < heights[best])))
                    { align = a; best = j; }
                }
            }
            if (best >= 0) flow[best] += flow[i];
        }
        return flow;
    }

    private static BiomeType RegionBiome(float elevation, BiomeType parentBiome)
    {
        if (elevation < SeaLevel) return BiomeType.Ocean;
        if (elevation > 0.55f) return BiomeType.Mountain;
        return parentBiome == BiomeType.Ocean ? BiomeType.Plains : parentBiome;
    }

    private static (BiomeType Biome, byte Danger) LocalBiome(float height, float temperature, float moisture)
        => BiomeClassifier.Classify(height, temperature, moisture);
}
