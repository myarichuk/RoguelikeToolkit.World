using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hierarchy;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Integration;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Sites;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Features;

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
        DepositCatalog? deposits = null,
        SiteCatalog? sites = null,
        MapAddress? address = null,
        TileOverride? tileOverride = null)
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
            Deposits = Array.Empty<DepositType>(),
            Address = address ?? MapAddress.ForPlanet(0, tileIndex),
            Sites = Array.Empty<PlacedSite>()
        };

        if (store.IsLayerRegistered<BiomeInfo>())
        {
            var local = store.GetSpan<BiomeInfo>()[tileIndex];
            info.HasBiome = true;
            info.Biome = local.Biome;
            info.DangerLevel = local.DangerLevel;
        }

        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;

        if (store.IsLayerRegistered<ElevationInfo>())
        {
            var elev = store.GetSpan<ElevationInfo>();
            info.HasElevation = true;
            info.Elevation = elev[tileIndex].Height;
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

            if (info.IsRiver && info.HasElevation)
            {
                // Local routing: only the tiles around the query are resolved,
                // never the whole planet (was ~10 ms per river tile at size 7).
                Span<int> scratch = stackalloc int[6];
                var receivers = new LocalReceivers(store);
                info.UpstreamTile = FindUpstream(store, hydro, tileIndex, receivers, scratch);
                if (info.UpstreamTile >= 0)
                    info.UpstreamCoord = store.GetGeoCoord(info.UpstreamTile);
                info.DownstreamTile = receivers.Of(tileIndex);
                if (info.DownstreamTile >= 0)
                    info.DownstreamCoord = store.GetGeoCoord(info.DownstreamTile);
                info.RiverSource = ClassifySource(store, hydro, tileIndex, receivers, hasClimate, climate);
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

        if (sites != null)
        {
            var onTile = sites.AtCell(tileIndex);
            if (onTile.Count > 0)
            {
                info.Sites = new PlacedSite[onTile.Count];
                for (int k = 0; k < onTile.Count; k++) info.Sites[k] = onTile[k];
            }
        }

        if (tileOverride != null)
        {
            if (tileOverride.Biome.HasValue)
            {
                info.HasBiome = true;
                info.Biome = tileOverride.Biome.Value;
            }
            if (tileOverride.DangerLevel.HasValue)
                info.DangerLevel = (byte)Math.Clamp(tileOverride.DangerLevel.Value, 0, 255);
        }

        return info;
    }

    // Highest-flow river neighbor draining into this tile (reverse-receiver lookup,
    // flat-safe via ComputeReceivers; deterministic tie-break by index.
    // -1 marks a head: no river water flows in from anywhere.
    private static int FindUpstream(WorldDataStore store, Span<HydrologyInfo> hydro, int tile, LocalReceivers receivers, Span<int> scratch)
    {
        int adjacent = store.GetAdjacent(tile, scratch);
        int best = -1;
        float bestFlow = float.NegativeInfinity;
        for (int k = 0; k < adjacent; k++)
        {
            int j = scratch[k];
            if (hydro[j].IsRiver != 1 || receivers.Of(j) != tile) continue;
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
        WorldDataStore store, Span<HydrologyInfo> hydro, int tile, LocalReceivers receivers, bool hasClimate, Span<ClimateInfo> climate)
    {
        Span<int> scratch = stackalloc int[6];
        var vectors = store.GetTileVectors();
        var elev = store.GetSpan<ElevationInfo>();
        int cur = tile;
        for (int steps = 0; steps <= store.TileCount; steps++)
        {
            int up = FindUpstream(store, hydro, cur, receivers, scratch);
            if (up < 0)
            {
                bool glacier = hasClimate
                    ? Glaciology.IsGlacierTile(vectors[cur], elev[cur].Height, climate[cur].Temperature, climate[cur].Precipitation)
                    : Glaciology.IsGlacierTile(vectors[cur], elev[cur].Height);
                return glacier ? RiverWaterSource.GlacierMelt : RiverWaterSource.Spring;
            }
            cur = up;
        }
        return RiverWaterSource.Spring;
    }

    // Memoized per-query receiver lookups over the hydrology routing surface.
    private sealed class LocalReceivers
    {
        private readonly WorldDataStore _store;
        private readonly Dictionary<int, int> _memo = new();

        public LocalReceivers(WorldDataStore store) => _store = store;

        public int Of(int tile)
        {
            if (_memo.TryGetValue(tile, out int r)) return r;
            r = Hydrography.ReceiverAt(_store, i => _store.GetRef<HydrologyInfo>(i).Surface, tile);
            _memo[tile] = r;
            return r;
        }
    }
}
