using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>Where a river tile's water comes from (head classification).</summary>
public enum RiverWaterSource : byte
{
    None = 0,
    /// <summary>Rain/groundwater-fed head: no glacier upstream.</summary>
    Spring = 1,
    /// <summary>The river's head sits on glaciated ground (meltwater-fed).</summary>
    GlacierMelt = 2
}

/// <summary>
/// Everything attached to one hex: biome, river reach with from/to neighbors,
/// water body, ranges, and glacier cover. Missing layers read as Has* = false
/// so partial pipelines stay queryable.
/// </summary>
public struct TileFeatureInfo
{
    public int TileIndex;
    public GeoCoord Coord;

    public bool HasBiome;
    public BiomeType Biome;
    public byte DangerLevel;

    public bool HasElevation;
    public float Elevation;

    public bool HasHydrology;
    public bool IsRiver;
    public float Flow;
    public int WaterBodyId;
    public float LakeDepth;

    /// <summary>Upstream river tile ("where the water comes from"), -1 at heads.</summary>
    public int UpstreamTile;
    public GeoCoord UpstreamCoord;
    /// <summary>Downstream tile ("where the water goes"), -1 in pit sinks.</summary>
    public int DownstreamTile;
    public GeoCoord DownstreamCoord;

    /// <summary>Head source of this tile's river; None for non-river tiles.</summary>
    public RiverWaterSource RiverSource;
    /// <summary>Id in <see cref="RiverCatalog"/>; -1 when unknown/absent.</summary>
    public int RiverId;
    /// <summary>Kind of the containing water body; null when unknown/absent.</summary>
    public WaterBodyKind? BodyKind;

    public bool InMountainRange;
    public bool InValley;
    public bool InCanyon;
    public bool InPlaya;
    public bool IsGlacier;
    public DepositType[] Deposits;
}

/// <summary>
/// Per-tile feature query over a <see cref="WorldDataStore"/> plus optional
/// sparse catalogs. Single-tile lookups (inspector UI, game queries); for
/// whole-map scans read the spans/catalogs directly instead.
/// </summary>
public static class TileFeatures
{
    public static TileFeatureInfo Query(
        WorldDataStore store,
        int tileIndex,
        RiverCatalog? rivers = null,
        WaterBodyCatalog? bodies = null,
        RangeCatalog? ranges = null,
        DepositCatalog? deposits = null)
    {
        if ((uint)tileIndex >= (uint)store.TileCount) throw new IndexOutOfRangeException();
        var info = new TileFeatureInfo
        {
            TileIndex = tileIndex,
            Coord = store.GetGeoCoord(tileIndex),
            WaterBodyId = -1,
            UpstreamTile = -1,
            DownstreamTile = -1,
            RiverId = -1,
            Deposits = Array.Empty<DepositType>()
        };

        if (store.IsLayerRegistered<LocalMapInfo>())
        {
            var local = store.GetSpan<LocalMapInfo>()[tileIndex];
            info.HasBiome = true;
            info.Biome = local.Biome;
            info.DangerLevel = local.DangerLevel;
        }

        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;

        float[]? heights = null;
        if (store.IsLayerRegistered<ElevationInfo>())
        {
            var elev = store.GetSpan<ElevationInfo>();
            info.HasElevation = true;
            info.Elevation = elev[tileIndex].Height;
            heights = new float[store.TileCount];
            for (int i = 0; i < heights.Length; i++) heights[i] = elev[i].Height;
            info.IsGlacier = hasClimate
                ? Glaciology.IsGlacierTile(store.GetTileVectors()[tileIndex], info.Elevation,
                    climate[tileIndex].Temperature, climate[tileIndex].Precipitation)
                : Glaciology.IsGlacierTile(store.GetTileVectors()[tileIndex], info.Elevation);
        }

        if (store.IsLayerRegistered<HydrologyInfo>())
        {
            var hydro = store.GetSpan<HydrologyInfo>();
            info.HasHydrology = true;
            info.IsRiver = hydro[tileIndex].IsRiver == 1;
            info.Flow = hydro[tileIndex].Flow;
            info.WaterBodyId = hydro[tileIndex].WaterBodyId;
            info.LakeDepth = hydro[tileIndex].LakeDepth;

            if (info.IsRiver && heights != null)
            {
                Span<int> scratch = stackalloc int[6];
                info.UpstreamTile = FindUpstream(store, hydro, heights, tileIndex, scratch);
                if (info.UpstreamTile >= 0)
                    info.UpstreamCoord = store.GetGeoCoord(info.UpstreamTile);
                info.DownstreamTile = Hydrography.LowestNeighbor(store, heights, tileIndex, scratch);
                if (info.DownstreamTile >= 0)
                    info.DownstreamCoord = store.GetGeoCoord(info.DownstreamTile);
                info.RiverSource = ClassifySource(store, hydro, heights, tileIndex, hasClimate, climate);
            }
        }

        // Paths include the terminal sea tile, which is not flagged as a river
        // reach, so catalog membership does not imply IsRiver.
        if (rivers != null)
        {
            foreach (var river in rivers.Rivers)
            {
                if (river.Path.Contains(tileIndex)) { info.RiverId = river.Id; break; }
            }
        }

        if (bodies != null && info.WaterBodyId >= 0)
        {
            foreach (var body in bodies.Bodies)
            {
                if (body.Tiles.Contains(tileIndex)) { info.BodyKind = body.Kind; break; }
            }
        }

        if (ranges != null)
        {
            foreach (var feat in ranges.Features)
            {
                if (!feat.Tiles.Contains(tileIndex)) continue;
                if (feat.Kind == RangeKind.MountainRange) info.InMountainRange = true;
                else if (feat.Kind == RangeKind.Valley) info.InValley = true;
                else if (feat.Kind == RangeKind.Canyon) info.InCanyon = true;
                else if (feat.Kind == RangeKind.Playa) info.InPlaya = true;
            }
        }

        if (deposits != null)
        {
            var found = deposits.AtTile(tileIndex);
            if (found.Count > 0)
            {
                info.Deposits = new DepositType[found.Count];
                for (int k = 0; k < found.Count; k++) info.Deposits[k] = found[k].Type;
            }
        }

        return info;
    }

    // Highest-flow higher river neighbor (deterministic tie-break by index).
    // -1 marks a head: no river water flows in from anywhere.
    private static int FindUpstream(WorldDataStore store, Span<HydrologyInfo> hydro, float[] heights, int tile, Span<int> scratch)
    {
        int adjacent = store.GetAdjacent(tile, scratch);
        int best = -1;
        float bestFlow = float.NegativeInfinity;
        for (int k = 0; k < adjacent; k++)
        {
            int j = scratch[k];
            if (hydro[j].IsRiver != 1 || heights[j] <= heights[tile]) continue;
            if (hydro[j].Flow > bestFlow || (hydro[j].Flow == bestFlow && j < best))
            {
                bestFlow = hydro[j].Flow;
                best = j;
            }
        }
        return best;
    }

    // Walk upstream to the head, then classify it: glacier heads are
    // meltwater-fed, everything else is rain/groundwater (spring).
    private static RiverWaterSource ClassifySource(
        WorldDataStore store, Span<HydrologyInfo> hydro, float[] heights, int tile, bool hasClimate, Span<ClimateInfo> climate)
    {
        Span<int> scratch = stackalloc int[6];
        var vectors = store.GetTileVectors();
        int cur = tile;
        for (int steps = 0; steps <= store.TileCount; steps++)
        {
            int up = FindUpstream(store, hydro, heights, cur, scratch);
            if (up < 0)
            {
                bool glacier = hasClimate
                    ? Glaciology.IsGlacierTile(vectors[cur], heights[cur], climate[cur].Temperature, climate[cur].Precipitation)
                    : Glaciology.IsGlacierTile(vectors[cur], heights[cur]);
                return glacier ? RiverWaterSource.GlacierMelt : RiverWaterSource.Spring;
            }
            cur = up;
        }
        return RiverWaterSource.Spring;
    }
}
