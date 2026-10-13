using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Biomes;

[Preserve] // created via reflection by WorldGenerationPipeline.Discover; keep it through Unity code stripping
[WorldGeneratorStage(20, Reads = new[] { typeof(ElevationInfo) }, ReadsOptional = new[] { typeof(ClimateInfo), typeof(HydrologyInfo), typeof(UserEditInfo) }, Writes = new[] { typeof(LocalMapInfo) })]
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
        bool hasPins = store.IsLayerRegistered<UserEditInfo>();
        var pins = hasPins ? store.GetSpan<UserEditInfo>() : default;

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
            double temperature, moisture, seasonality = 0.0, phase = 0.0;
            if (hasClimate)
            {
                temperature = climate[i].Temperature;
                moisture = climate[i].Precipitation;
                seasonality = climate[i].RainSeasonality;
                phase = climate[i].RainPhase;
            }
            else
            {
                temperature = ClimateModel.Temperature(vectors[i], Math.Abs(vectors[i].ToGeoCoord().Latitude), height, ClimateModel.DefaultLapseRate, null);
                moisture = SphereNoise.Fbm(vectors[i] * 2.0 + moistureOffset, Seed + 1000) * 0.5 + 0.5;
            }

            // Ocean/land decision ladder is shared with local maps; glacier and
            // canyon override land only. Glacier outranks mountain: a high, wet,
            // cold tile is ice, not rock; dry cold peaks stay Mountain.
            BiomeClassifier.AridSite site = default;
            // Salt crust comes from the lake solver: a playa floor or the lakebed ringing a terminal lake.
            // An open lake flushes through its outlet and leaves none, however dry the country around it.
            bool saline = hasHydro && hydro[i].Saline == 1 && hydro[i].LakeDepth <= 0f;
            bool arid = height >= ElevationGenerationStage.SeaLevel
                && (BiomeClassifier.IsArid(temperature, moisture) || BiomeClassifier.IsColdArid(temperature, moisture));
            if (saline || arid)
            {
                double relief = 0.0;
                int adjacent = store.GetAdjacent(i, neighbors);
                for (int k = 0; k < adjacent; k++)
                    relief = Math.Max(relief, Math.Abs(height - elev[neighbors[k]].Height));
                site = new BiomeClassifier.AridSite(relief, saline);
            }
            var (biome, danger) = BiomeClassifier.Classify(height, temperature, moisture, site, seasonality, phase);
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
            if (hasPins && (pins[i].Flags & UserEditFlags.PinBiome) != 0) ApplyPin(ref span[i], pins[i].PinBiome);
        }

        if (SmoothBiomes && SmoothingPasses > 0)
            SmoothMoistureBiomes(store, span, SmoothingPasses);

        // Smoothing votes over whatever it sees, painted ground included; the author's word is final.
        if (hasPins)
            for (int i = 0; i < span.Length; i++)
                if ((pins[i].Flags & UserEditFlags.PinBiome) != 0 && span[i].Biome != pins[i].PinBiome) ApplyPin(ref span[i], pins[i].PinBiome);
    }

    private static void ApplyPin(ref LocalMapInfo info, BiomeType pinned)
    {
        // Re-base the danger the way smoothing does: biome base plus whatever per-tile jitter was rolled.
        int jitter = Math.Max(0, info.DangerLevel - BiomeClassifier.BaseDanger(info.Biome));
        info.Biome = pinned;
        info.DangerLevel = (byte)Math.Min(5, BiomeClassifier.BaseDanger(pinned) + jitter);
    }

    // Candidate order doubles as the tie-break, as it always has for the moisture set.
    private static readonly BiomeType[] MoistureFamily = { BiomeType.Plains, BiomeType.Forest, BiomeType.Jungle, BiomeType.Swamp, BiomeType.Savanna, BiomeType.Mediterranean };
    // Hot arid sub-types differ by a relief or height cut that crosses tile by tile; salt flats are
    // a hydrology feature and cold desert a climate band, so neither is smoothed into its neighbours.
    private static readonly BiomeType[] HotAridFamily = { BiomeType.Desert, BiomeType.RockDesert, BiomeType.Badlands };

    private static BiomeType[]? FamilyOf(BiomeType biome)
        => Array.IndexOf(MoistureFamily, biome) >= 0 ? MoistureFamily
         : Array.IndexOf(HotAridFamily, biome) >= 0 ? HotAridFamily
         : null;

    /// <summary>
    /// Neighbor-majority cleanup restricted to two families of close-threshold biomes that speckle
    /// on noisy inputs: the moisture set (plains/forest/jungle/swamp/savanna/mediterranean) and the hot arid set
    /// (desert/rock desert/badlands). A tile flips only to a member of its own family, and only when
    /// 4+ neighbors agree on it; each pass reads from a snapshot so the result is independent of
    /// tile iteration order.
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
                var family = FamilyOf(current[i]);
                if (family == null) continue;
                int adjacent = store.GetAdjacent(i, neighbors);
                BiomeType best = current[i];
                int bestCount = -1;
                foreach (var candidate in family)
                {
                    int count = 0;
                    for (int k = 0; k < adjacent; k++)
                        if (current[neighbors[k]] == candidate) count++;
                    if (count > bestCount) { best = candidate; bestCount = count; }
                }
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
                    - BiomeClassifier.BaseDanger(info.Biome)
                    + BiomeClassifier.BaseDanger((BiomeType)flipTo[i]);
                info.DangerLevel = (byte)Math.Clamp(rebased, 0, 5);
                info.Biome = (BiomeType)flipTo[i];
                span[i] = info;
                current[i] = info.Biome;
                changed = true;
            }
            if (!changed) break;
        }
    }

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
