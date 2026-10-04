using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Unified feature kinds for spatial queries.</summary>
public enum FeatureKind : byte
{
    River = 0,
    WaterBody = 1,
    Range = 2,
    Glacier = 3,
    Deposit = 4,
    Site = 5,
}

/// <summary>
/// Bucketed nearest-feature index over planet tile centers. Tiles are grouped
/// into a coarse lat/lon grid (the same bucketing idea as
/// <see cref="WorldDataStore.GetTileIndex"/>); a query orders cells by
/// center alignment and scans tiles cell-by-cell, stopping once the best
/// remaining cell center provably cannot beat the current best. The stop bound
/// is exact, so results always agree with an exhaustive scan.
/// Planet tier only: a <see cref="MapAddress"/> resolves to its parent planet
/// tile (<see cref="MapAddress.WorldTileIndex"/>).
/// </summary>
public sealed class SpatialIndex
{
    // 10-degree cells; worst-case tile-to-cell-center angle is ~7.08 degrees
    // (equatorial corner), so this slack keeps the early-stop bound exact.
    private const int LatCells = 18;
    private const int LonCells = 36;
    private const double Slack = 0.124;

    private readonly WorldDataStore _store;
    private readonly Vector3D[] _vectors;
    private readonly Vector3D[] _cellCenters;
    private readonly int[] _cellOffsets;
    private readonly int[] _cellTiles;
    private readonly Dictionary<FeatureKind, int[]> _tilesByKind = new();
    private readonly Dictionary<FeatureKind, bool[]> _masksByKind = new();
    private readonly bool _hasDeposits;
    private readonly bool[] _depositMask;
    private readonly DepositType[] _depositTypeOfTile;
    private readonly List<(SiteKind Kind, int TileIndex, PlacedSite Site)> _sites = new();

    public WorldDataStore Store => _store;

    public SpatialIndex(
        WorldDataStore store,
        RiverCatalog? rivers = null,
        WaterBodyCatalog? bodies = null,
        RangeCatalog? ranges = null,
        DepositCatalog? deposits = null,
        SiteCatalog? sites = null,
        Func<int, bool>? isWater = null,
        Func<int, bool>? isGlacier = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        int n = store.TileCount;
        _vectors = store.TileVectorArray;

        isWater ??= DefaultIsWater(store);
        isGlacier ??= DefaultIsGlacier(store);

        var riverMask = new bool[n];
        if (rivers != null)
            foreach (var r in rivers.Rivers)
                foreach (int t in r.Path)
                    if (t >= 0 && t < n) riverMask[t] = true;

        // Plus sub-resolution trickles: lone IsRiver tiles never form a catalog
        // reach (Path.Count >= 2 contract) but must still resolve via
        // NearestFeature instead of vanishing from the index.
        if (store.IsLayerRegistered<HydrologyInfo>())
        {
            var hydro = store.GetSpan<HydrologyInfo>();
            for (int i = 0; i < n; i++)
                if (hydro[i].IsRiver == 1) riverMask[i] = true;
        }
        var waterMask = new bool[n];
        for (int i = 0; i < n; i++)
            if (isWater(i)) waterMask[i] = true;
        if (bodies != null)
            foreach (var b in bodies.Bodies)
                foreach (int t in b.Tiles)
                    if (t >= 0 && t < n) waterMask[t] = true;

        var rangeMask = new bool[n];
        if (ranges != null)
            foreach (var f in ranges.Features)
                foreach (int t in f.Tiles)
                    if (t >= 0 && t < n) rangeMask[t] = true;

        var glacierMask = new bool[n];
        for (int i = 0; i < n; i++)
            if (isGlacier(i)) glacierMask[i] = true;

        _depositMask = new bool[n];
        _depositTypeOfTile = new DepositType[n];
        if (deposits != null)
        {
            foreach (var d in deposits.Deposits)
            {
                if ((uint)d.TileIndex >= (uint)n) continue;
                _depositMask[d.TileIndex] = true;
                _depositTypeOfTile[d.TileIndex] = d.Type;
                _hasDeposits = true;
            }
        }

        var siteMask = new bool[n];
        if (sites != null)
        {
            foreach (var s in sites.Sites)
            {
                if (s == null || (uint)s.CellIndex >= (uint)n) continue;
                siteMask[s.CellIndex] = true;
                _sites.Add((s.Kind, s.CellIndex, s));
            }
        }

        // Ascending tile lists fall out of one scan per mask (no sort, no hash sets).
        _masksByKind[FeatureKind.River] = riverMask;
        _masksByKind[FeatureKind.WaterBody] = waterMask;
        _masksByKind[FeatureKind.Range] = rangeMask;
        _masksByKind[FeatureKind.Glacier] = glacierMask;
        _masksByKind[FeatureKind.Deposit] = _depositMask;
        _masksByKind[FeatureKind.Site] = siteMask;
        foreach (var (kind, mask) in _masksByKind)
            _tilesByKind[kind] = TilesOfMask(mask);

        // Bucket every tile center into the lat/lon grid.
        _cellCenters = new Vector3D[LatCells * LonCells];
        for (int r = 0; r < LatCells; r++)
            for (int c = 0; c < LonCells; c++)
                _cellCenters[r * LonCells + c] = Vector3D.FromGeoCoord(new GeoCoord(
                    -90.0 + (r + 0.5) * (180.0 / LatCells),
                    -180.0 + (c + 0.5) * (360.0 / LonCells)));

        var sizes = new int[LatCells * LonCells];
        var tileCell = new int[n];
        for (int i = 0; i < n; i++)
        {
            int cell = CellOf(store.GetGeoCoord(i));
            tileCell[i] = cell;
            sizes[cell]++;
        }
        _cellOffsets = new int[LatCells * LonCells + 1];
        for (int c = 0; c < sizes.Length; c++)
            _cellOffsets[c + 1] = _cellOffsets[c] + sizes[c];
        _cellTiles = new int[n];
        var cursors = new int[sizes.Length];
        Array.Copy(_cellOffsets, cursors, sizes.Length);
        for (int i = 0; i < n; i++)
            _cellTiles[cursors[tileCell[i]]++] = i;
    }

    /// <summary>Tiles carrying the given feature kind, ascending.</summary>
    public IReadOnlyList<int> TilesOfKind(FeatureKind kind)
        => _tilesByKind.TryGetValue(kind, out var tiles) ? tiles : Array.Empty<int>();

    /// <summary>Nearest tile carrying <paramref name="kind"/>, or null when the set is empty.</summary>
    public (int TileIndex, double DistanceKm)? NearestFeature(GeoCoord from, FeatureKind kind)
    {
        if (!_tilesByKind.TryGetValue(kind, out var tiles) || tiles.Length == 0) return null;
        if (kind == FeatureKind.Deposit && !_hasDeposits) return null;
        return NearestInMask(from, _masksByKind[kind]);
    }

    /// <summary>
    /// True when any tile carrying <paramref name="kind"/> (other than
    /// <paramref name="exceptTile"/>) lies within the angle whose cosine is
    /// <paramref name="cosThreshold"/> of <paramref name="from"/>. Exact: cells
    /// that provably cannot hold a qualifying tile (same slack bound as
    /// <see cref="NearestInMask"/>) are skipped; no sort, no allocation.
    /// </summary>
    public bool AnyWithin(Vector3D from, FeatureKind kind, double cosThreshold, int exceptTile = -1)
    {
        if (!_masksByKind.TryGetValue(kind, out var mask)) return false;
        for (int c = 0; c < _cellCenters.Length; c++)
        {
            if (Vector3D.Dot(from, _cellCenters[c]) + Slack < cosThreshold) continue;
            for (int k = _cellOffsets[c]; k < _cellOffsets[c + 1]; k++)
            {
                int t = _cellTiles[k];
                if (t == exceptTile || !mask[t]) continue;
                if (Vector3D.Dot(from, _vectors[t]) >= cosThreshold) return true;
            }
        }
        return false;
    }

    /// <summary>Nearest tile carrying <paramref name="kind"/> to a map address (via its planet tile).</summary>
    public (int TileIndex, double DistanceKm)? NearestFeature(MapAddress address, FeatureKind kind)
        => NearestFeature(_store.GetGeoCoord(address.WorldTileIndex), kind);

    /// <summary>Nearest deposit tile, optionally filtered by type.</summary>
    public (int TileIndex, double DistanceKm)? NearestDeposit(GeoCoord from, DepositType? type = null)
    {
        if (!_hasDeposits) return null;
        if (type == null) return NearestInMask(from, _depositMask);
        return NearestInMask(from, _depositMask, type.Value);
    }

    /// <summary>Nearest deposit tile to a map address (via its planet tile).</summary>
    public (int TileIndex, double DistanceKm)? NearestDeposit(MapAddress address, DepositType? type = null)
        => NearestDeposit(_store.GetGeoCoord(address.WorldTileIndex), type);

    /// <summary>Nearest injected site of the given kind, or null when none exists.</summary>
    public (PlacedSite Site, double DistanceKm)? NearestSite(GeoCoord from, SiteKind kind)
    {
        var target = Vector3D.FromGeoCoord(from);
        PlacedSite? best = null;
        double bestDot = double.NegativeInfinity;
        foreach (var (k, tile, site) in _sites)
        {
            if (k != kind) continue;
            double dot = Vector3D.Dot(target, _vectors[tile]);
            if (dot > bestDot) { bestDot = dot; best = site; }
        }
        if (best == null) return null;
        return (best, DotToKm(bestDot));
    }

    /// <summary>Nearest injected site of the given kind to a map address (via its planet tile).</summary>
    public (PlacedSite Site, double DistanceKm)? NearestSite(MapAddress address, SiteKind kind)
        => NearestSite(_store.GetGeoCoord(address.WorldTileIndex), kind);

    // Exact bucketed search: cells in descending center-alignment order, stop
    // when no remaining cell center can beat the best tile (slack covers the
    // worst tile-to-center angle inside a cell).
    private (int TileIndex, double DistanceKm)? NearestInMask(
        GeoCoord from, bool[] mask, DepositType? depositType = null)
    {
        var target = Vector3D.FromGeoCoord(from);
        int cellCount = _cellCenters.Length;
        Span<int> order = stackalloc int[cellCount];
        Span<double> dots = stackalloc double[cellCount];
        for (int c = 0; c < cellCount; c++)
        {
            order[c] = c;
            dots[c] = -Vector3D.Dot(target, _cellCenters[c]); // negated: ascending key sort == nearest first
        }
        MemoryExtensions.Sort(dots, order);

        int best = -1;
        double bestDot = double.NegativeInfinity;
        for (int p = 0; p < order.Length; p++)
        {
            int c = order[p];
            // dots was sorted alongside order: position p holds this cell's negated alignment.
            if (best >= 0 && -dots[p] + Slack <= bestDot) break;
            for (int k = _cellOffsets[c]; k < _cellOffsets[c + 1]; k++)
            {
                int t = _cellTiles[k];
                if (!mask[t]) continue;
                if (depositType.HasValue && _depositTypeOfTile[t] != depositType.Value) continue;
                double dot = Vector3D.Dot(target, _vectors[t]);
                if (dot > bestDot) { bestDot = dot; best = t; }
            }
        }
        return best < 0 ? null : (best, DotToKm(bestDot));
    }

    private static double DotToKm(double dot)
        => DetMath.Acos(Math.Clamp(dot, -1.0, 1.0)) * World.EarthRadiusKm;

    private static int CellOf(GeoCoord coord)
    {
        int lat = (int)((coord.Latitude + 90.0) / 180.0 * LatCells);
        if (lat < 0) lat = 0;
        else if (lat >= LatCells) lat = LatCells - 1;
        int lon = (int)((coord.Longitude + 180.0) / 360.0 * LonCells) % LonCells;
        if (lon < 0) lon += LonCells;
        return lat * LonCells + lon;
    }

    private static int[] TilesOfMask(bool[] mask)
    {
        int count = 0;
        foreach (bool b in mask) if (b) count++;
        var tiles = new int[count];
        int w = 0;
        for (int i = 0; i < mask.Length; i++) if (mask[i]) tiles[w++] = i;
        return tiles;
    }

    private static Func<int, bool> DefaultIsWater(WorldDataStore store)
    {
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        if (!hasElev && !hasHydro) return _ => false;
        return tile =>
        {
            if (hasElev && store.GetSpan<ElevationInfo>()[tile].Height < ElevationGenerationStage.SeaLevel)
                return true;
            return hasHydro && store.GetSpan<HydrologyInfo>()[tile].LakeDepth > 0f;
        };
    }

    private static Func<int, bool> DefaultIsGlacier(WorldDataStore store)
    {
        if (!store.IsLayerRegistered<ElevationInfo>()) return _ => false;
        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        return tile =>
        {
            float h = store.GetSpan<ElevationInfo>()[tile].Height;
            var v = store.GetTileVectors()[tile];
            if (hasClimate)
            {
                var c = store.GetSpan<ClimateInfo>()[tile];
                return Glaciology.IsGlacierTile(v, h, c.Temperature, c.Precipitation);
            }
            return Glaciology.IsGlacierTile(v, h);
        };
    }
}
