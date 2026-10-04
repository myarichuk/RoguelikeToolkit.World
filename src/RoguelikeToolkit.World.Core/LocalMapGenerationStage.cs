using System;

namespace RoguelikeToolkit.World.Core;

[WorldGeneratorStage(20, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(ClimateInfo), typeof(HydrologyInfo) }, Writes = new[] { typeof(LocalMapInfo) })]
public class LocalMapGenerationStage : IWorldGeneratorStage, ISeededStage
{
    public int Seed { get; set; } = 42;

    /// <summary>
    /// When true, moisture-threshold speckle (jungle/forest/swamp/plains
    /// single-tile enclaves) is cleaned with neighbor-majority passes.
    /// Elevation-gated biomes (ocean, glacier, canyon, mountain) and
    /// temperature-gated ones (tundra, desert) are never flipped.
    /// </summary>
    public bool SmoothBiomes { get; set; } = true;

    /// <summary>Majority-vote cleanup passes over the moisture biome set.</summary>
    public int SmoothingPasses { get; set; } = 2;

    /// <summary>Must match <see cref="HydrologyStage.RiverThresholdScale"/> so canyon biomes agree with rivers (WorldBuilder wires it).</summary>
    public float RiverThresholdScale { get; set; } = 1f;

    public LocalMapGenerationStage()
    {
    }

    public LocalMapGenerationStage(int seed = 42)
    {
        Seed = seed;
    }

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var span = store.GetSpan<LocalMapInfo>();
        var elev = store.GetSpan<ElevationInfo>();
        bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
        var climate = hasClimate ? store.GetSpan<ClimateInfo>() : default;
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;

        var vectors = store.GetTileVectors();

        // Fallback moisture field when ClimateStage did not run (backwards
        // compatible with pipelines that only register elevation + local).
        var moistureOffset = new Vector3D(13.7, -7.3, 5.1);

        var beds = new float[store.TileCount];
        for (int i = 0; i < beds.Length; i++) beds[i] = elev[i].Height;
        float riverThreshold = Hydrography.RiverThreshold(store.TileCount, RiverThresholdScale);
        Span<int> neighbors = stackalloc int[6];

        for (int i = 0; i < span.Length; i++)
        {
            double height = elev[i].Height;
            double temperature, moisture;
            if (hasClimate)
            {
                temperature = climate[i].Temperature;
                moisture = climate[i].Precipitation;
            }
            else
            {
                temperature = 1.0 - Math.Abs(vectors[i].ToGeoCoord().Latitude) / 90.0;
                temperature -= Math.Max(0.0, height) * 0.35;
                moisture = SphereNoise.Fbm(vectors[i] * 2.0 + moistureOffset, Seed + 1000) * 0.5 + 0.5;
            }

            // Ocean/land decision ladder is shared with local maps; glacier and
            // canyon override land only. Glacier outranks mountain: a high, wet,
            // cold tile is ice, not rock; dry cold peaks stay Mountain.
            var (biome, danger) = BiomeClassifier.Classify(height, temperature, moisture);
            if (biome != BiomeType.Ocean)
            {
                bool glacier = hasClimate
                    ? Glaciology.IsGlacierTile(vectors[i], elev[i].Height, (float)temperature, (float)moisture)
                    : Glaciology.IsGlacierTile(vectors[i], elev[i].Height);
                if (glacier)
                {
                    biome = BiomeType.Glacier;
                    danger = 2;
                }
                else if (IsCanyonTile(store, elev, hydro, hasHydro, moisture, i, riverThreshold, beds, neighbors))
                {
                    biome = BiomeType.Canyon;
                    danger = 3;
                }
            }

            // Small deterministic per-tile jitter so neighboring same-biome tiles vary.
            var r = Rng.Create(Seed + 7, i);
            danger = (byte)Math.Min(5, danger + (int)r.NextUInt(2));

            // The per-tile seed is not stored: it is a pure function of (world seed,
            // tile index). See World.TileSeed / Rng.DeriveTileSeed.
            span[i] = new LocalMapInfo
            {
                Biome = biome,
                DangerLevel = danger
            };
        }

        if (SmoothBiomes && SmoothingPasses > 0)
            SmoothMoistureBiomes(store, span, SmoothingPasses);
    }

    /// <summary>
    /// Neighbor-majority cleanup restricted to the moisture-threshold biome
    /// set (plains/forest/jungle/swamp), whose close thresholds speckle on
    /// noisy precipitation. A tile flips only when 4+ neighbors agree on a
    /// different set member; each pass reads from a snapshot so the result is
    /// independent of tile iteration order.
    /// </summary>
    public static void SmoothMoistureBiomes(WorldDataStore store, Span<LocalMapInfo> span, int passes)
    {
        var current = new BiomeType[span.Length];
        for (int i = 0; i < span.Length; i++) current[i] = span[i].Biome;
        var flipTo = new int[span.Length];
        Span<int> neighbors = stackalloc int[6];

        for (int pass = 0; pass < passes; pass++)
        {
            Array.Fill(flipTo, -1);
            for (int i = 0; i < span.Length; i++)
            {
                if (!IsMoistureBiome(current[i])) continue;
                int adjacent = store.GetAdjacent(i, neighbors);
                int plains = 0, forest = 0, jungle = 0, swamp = 0;
                for (int k = 0; k < adjacent; k++)
                {
                    switch (current[neighbors[k]])
                    {
                        case BiomeType.Plains: plains++; break;
                        case BiomeType.Forest: forest++; break;
                        case BiomeType.Jungle: jungle++; break;
                        case BiomeType.Swamp: swamp++; break;
                    }
                }
                BiomeType best = current[i];
                int bestCount = -1;
                if (plains > bestCount) { best = BiomeType.Plains; bestCount = plains; }
                if (forest > bestCount) { best = BiomeType.Forest; bestCount = forest; }
                if (jungle > bestCount) { best = BiomeType.Jungle; bestCount = jungle; }
                if (swamp > bestCount) { best = BiomeType.Swamp; bestCount = swamp; }
                if (best != current[i] && bestCount >= 4)
                    flipTo[i] = (int)best;
            }
            // Jacobi update: the whole pass reads the pre-pass snapshot, so
            // the result is independent of tile iteration order.
            bool changed = false;
            for (int i = 0; i < span.Length; i++)
            {
                if (flipTo[i] < 0) continue;
                var info = span[i];
                // Danger = biome base + per-tile jitter. Re-base it so a flipped
                // tile does not keep the old biome's danger.
                int rebased = info.DangerLevel
                    - BiomeClassifier.MoistureBiomeDanger(info.Biome)
                    + BiomeClassifier.MoistureBiomeDanger((BiomeType)flipTo[i]);
                info.DangerLevel = (byte)Math.Clamp(rebased, 0, 5);
                info.Biome = (BiomeType)flipTo[i];
                span[i] = info;
                current[i] = info.Biome;
                changed = true;
            }
            if (!changed) break;
        }
    }

    private static bool IsMoistureBiome(BiomeType biome)
        => biome == BiomeType.Plains || biome == BiomeType.Forest
            || biome == BiomeType.Jungle || biome == BiomeType.Swamp;

    private static bool IsCanyonTile(
        WorldDataStore store,
        Span<ElevationInfo> elev,
        Span<HydrologyInfo> hydro,
        bool hasHydro,
        double moisture,
        int tile,
        float riverThreshold,
        float[] beds,
        Span<int> neighbors)
    {
        // Canyons need a cataloged-strength river; without hydrology the gate
        // cannot be evaluated, so no canyon biomes are emitted.
        if (!hasHydro || hydro[tile].IsRiver != 1) return false;
        if (elev[tile].Height < ElevationGenerationStage.SeaLevel) return false;
        float best = hydro[tile].Surface;
        int adjacent = store.GetAdjacent(tile, neighbors);
        for (int k = 0; k < adjacent; k++)
        {
            float s = hydro[neighbors[k]].Surface;
            if (s < best) best = s;
        }
        float slope = hydro[tile].Surface - best;
        float relief = CanyonAnalysis.Relief(store, beds, tile, neighbors);
        return CanyonAnalysis.IsCanyonTile(hydro[tile].Flow, riverThreshold, moisture, relief, slope);
    }
}
