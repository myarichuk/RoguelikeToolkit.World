using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Integration;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Queries;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Settlement;

public static class CitySiteScorer
{
    public static List<CitySiteScore> Score(
        WorldDataStore store,
        RiverCatalog rivers,
        WaterBodyCatalog waters,
        CitySiteFilter filter,
        QueryOptions? options = null,
        int worldSeed = 0)
    {
        var elev = store.IsLayerRegistered<ElevationInfo>() ? store.GetSpan<ElevationInfo>() : default;
        var locals = store.IsLayerRegistered<BiomeInfo>() ? store.GetSpan<BiomeInfo>() : default;
        var hydro = store.IsLayerRegistered<HydrologyInfo>() ? store.GetSpan<HydrologyInfo>() : default;
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        bool hasLocals = store.IsLayerRegistered<BiomeInfo>();
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();

        var riverTiles = new HashSet<int>();
        foreach (var r in rivers.Rivers)
            foreach (var t in r.Path) riverTiles.Add(t);

        // Sea ice is a pure function of the climate (no climate layer: all None). The polar pack never thaws, so it
        // is not water a settlement can use (no harbour, no boats, no fishing); seasonal ice is, but closes the coast
        // for part of the year (see SettlementScoring.WinterIce).
        var ice = SeaIce.Mask(store);
        var waterTiles = new HashSet<int>(riverTiles);
        for (int w = 0; w < store.TileCount; w++)
        {
            if (ice[w] == SeaIceKind.Perennial) continue;
            if (hasHydro && hydro[w].WaterBodyId >= 0) waterTiles.Add(w);
            else if (hasElev && elev[w].Height < 0f) waterTiles.Add(w);
        }
        var tileVectors = store.GetTileVectors();
        // Exact bucketed "any water within radius" instead of scanning every water
        // tile (mostly ocean) per candidate: that was O(land x water).
        SpatialIndex? waterIndex = null;
        double radiusKm = filter.FreshWaterRadiusKm;
        bool useRadius = !double.IsNaN(radiusKm) && radiusKm > 0 && radiusKm < Math.PI * World.EarthRadiusKm;
        double cosThreshold = useRadius ? DetMath.Cos(radiusKm / World.EarthRadiusKm) : double.NaN;
        bool radiusCoversGlobe = !double.IsNaN(radiusKm) && radiusKm >= Math.PI * World.EarthRadiusKm;

        var result = new List<CitySiteScore>();
        Span<int> neighbors = stackalloc int[6];

        for (int i = 0; i < store.TileCount; i++)
        {
            float h = hasElev ? elev[i].Height : 0f;
            if (h < filter.MinElevation || h > filter.MaxElevation) continue;

            BiomeType biome = hasLocals ? locals[i].Biome : BiomeType.Plains;
            if (filter.AllowedBiomes != null && filter.AllowedBiomes.Count > 0 && !filter.AllowedBiomes.Contains(biome)) continue;
            if (biome == BiomeType.Ocean || biome == BiomeType.Glacier) continue;

            byte danger = hasLocals ? locals[i].DangerLevel : (byte)0;
            float dangerDelta = 0f, habDelta = 0f;
            if (options?.History != null && options.History.TryGetTileModifier(i, out var mod))
            {
                dangerDelta = mod.DangerDelta;
                habDelta = mod.HabitabilityDelta;
            }
            if (options?.Materialized != null
                && options.Materialized.TryGetOverride(MapAddress.ForPlanet(worldSeed, i), out var tileOverride)
                && tileOverride != null)
            {
                if (tileOverride.Biome.HasValue) biome = tileOverride.Biome.Value;
                if (tileOverride.DangerLevel.HasValue) danger = (byte)Math.Clamp(tileOverride.DangerLevel.Value, 0, 255);
            }
            int effDanger = (int)danger + (int)MathF.Round(dangerDelta);
            if (effDanger > filter.MaxDanger) continue;

            // Fresh water: water on tile/neighbor, else any water within FreshWaterRadiusKm.
            bool water = waterTiles.Contains(i);
            // Tile-level hydro/sea membership is already in waterTiles.
            bool touchesSeasonalIce = false, touchesOtherWater = water;
            if (!water)
            {
                int adj = store.GetAdjacent(i, neighbors);
                for (int k = 0; k < adj; k++)
                {
                    int n = neighbors[k];
                    if (!waterTiles.Contains(n)) continue;
                    water = true;
                    if (ice[n] == SeaIceKind.Seasonal) touchesSeasonalIce = true;
                    else touchesOtherWater = true;
                }
            }
            // Winter-bound only when ice is all the water there is: one open sea tile, river or lake keeps the harbour.
            bool winterIce = touchesSeasonalIce && !touchesOtherWater;
            if (!water && waterTiles.Count > 0)
            {
                if (radiusCoversGlobe) water = true;
                else if (useRadius)
                {
                    waterIndex ??= new SpatialIndex(store, isWater: waterTiles.Contains, isGlacier: _ => false);
                    water = waterIndex.AnyWithin(tileVectors[i], FeatureKind.WaterBody, cosThreshold, exceptTile: i);
                }
            }
            if (filter.RequireFreshWater && !water) continue;

            var reasons = new List<string>();
            double score = SettlementScoring.Score(water, biome, h, effDanger, habDelta, reasons, winterIce);
            if (habDelta != 0) reasons.Add("history");
            if (dangerDelta != 0) reasons.Add("past-events");

            result.Add(new CitySiteScore
            {
                TileIndex = i,
                Coord = store.GetGeoCoord(i),
                Score = score,
                Biome = biome,
                Elevation = h,
                Reasons = string.Join(",", reasons)
            });
        }

        // Scores are discrete and heavily tied; List.Sort is unstable, so break
        // ties by tile index to keep TopN deterministic.
        result.Sort((a, b) =>
        {
            int c = b.Score.CompareTo(a.Score);
            return c != 0 ? c : a.TileIndex.CompareTo(b.TileIndex);
        });
        int topN = Math.Max(0, filter.TopN);
        if (result.Count > topN) result.RemoveRange(topN, result.Count - topN);
        return result;
    }
}
