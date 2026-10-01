using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Filter for city-site search. Null/empty means "no constraint".</summary>
public sealed class CitySiteFilter
{
    public float MinElevation { get; init; } = 0.01f;
    public float MaxElevation { get; init; } = 0.6f;
    public HashSet<BiomeType>? AllowedBiomes { get; init; }
    public byte MaxDanger { get; init; } = 3;
    public bool RequireFreshWater { get; init; } = true;
    public double FreshWaterRadiusKm { get; init; } = 600.0;
    public int TopN { get; init; } = 10;
}

public sealed class CitySiteScore
{
    public int TileIndex { get; init; }
    public GeoCoord Coord { get; init; }
    public double Score { get; init; }
    public BiomeType Biome { get; init; }
    public float Elevation { get; init; }
    public string Reasons { get; init; } = string.Empty;
}

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
        var locals = store.IsLayerRegistered<LocalMapInfo>() ? store.GetSpan<LocalMapInfo>() : default;
        var hydro = store.IsLayerRegistered<HydrologyInfo>() ? store.GetSpan<HydrologyInfo>() : default;
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        bool hasLocals = store.IsLayerRegistered<LocalMapInfo>();
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();

        var riverTiles = new HashSet<int>();
        foreach (var r in rivers.Rivers)
            foreach (var t in r.Path) riverTiles.Add(t);

        var waterTiles = new HashSet<int>(riverTiles);
        for (int w = 0; w < store.TileCount; w++)
        {
            if (hasHydro && hydro[w].WaterBodyId >= 0) waterTiles.Add(w);
            else if (hasElev && elev[w].Height < 0f) waterTiles.Add(w);
        }
        var tileVectors = store.GetTileVectors();
        double radiusKm = filter.FreshWaterRadiusKm;
        bool useRadius = !double.IsNaN(radiusKm) && radiusKm > 0 && radiusKm < Math.PI * World.EarthRadiusKm;
        double cosThreshold = useRadius ? Math.Cos(radiusKm / World.EarthRadiusKm) : double.NaN;
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
            if (!water)
            {
                int adj = store.GetAdjacent(i, neighbors);
                for (int k = 0; k < adj && !water; k++)
                {
                    if (waterTiles.Contains(neighbors[k])) water = true;
                }
            }
            if (!water && waterTiles.Count > 0)
            {
                if (radiusCoversGlobe) water = true;
                else if (useRadius)
                {
                    var candidate = tileVectors[i];
                    foreach (int wt in waterTiles)
                    {
                        if (wt == i) continue;
                        if (Vector3D.Dot(candidate, tileVectors[wt]) >= cosThreshold) { water = true; break; }
                    }
                }
            }
            if (filter.RequireFreshWater && !water) continue;

            double score = 1.0;
            var reasons = new List<string>();
            if (water) { score += 1.0; reasons.Add("water"); }
            if (biome is BiomeType.Plains or BiomeType.Forest) { score += 0.5; reasons.Add("fertile"); }
            if (biome is BiomeType.Tundra or BiomeType.Desert or BiomeType.Mountain or BiomeType.Canyon) { score -= 0.5; reasons.Add("harsh"); }
            if (h > 0.35f) { score -= 0.3; reasons.Add("high"); }
            score -= effDanger * 0.2;
            score += habDelta;
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

        result.Sort((a, b) => b.Score.CompareTo(a.Score));
        int topN = Math.Max(0, filter.TopN);
        if (result.Count > topN) result.RemoveRange(topN, result.Count - topN);
        return result;
    }
}
