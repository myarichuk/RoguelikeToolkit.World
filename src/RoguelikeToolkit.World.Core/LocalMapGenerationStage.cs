using System;

namespace RoguelikeToolkit.World.Core;

[WorldGeneratorStage(20)]
public class LocalMapGenerationStage : IWorldGeneratorStage, ISeededStage
{
    public int Seed { get; set; } = 42;

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
        var vectors = store.GetTileVectors();

        // Offset moisture sampling so it decorrelates from the elevation noise field.
        var moistureOffset = new Vector3D(13.7, -7.3, 5.1);

        for (int i = 0; i < span.Length; i++)
        {
            double height = elev[i].Height;
            var geo = vectors[i].ToGeoCoord();

            // Simple climate: temperature falls off with latitude and elevation
            // (lapse rate), moisture is an independent seamless noise field.
            double temperature = 1.0 - Math.Abs(geo.Latitude) / 90.0;
            temperature -= Math.Max(0.0, height) * 0.35;
            double moisture = SphereNoise.Fbm(vectors[i] * 2.0 + moistureOffset, Seed + 1000) * 0.5 + 0.5;

            BiomeType biome;
            byte danger;
            if (height < ElevationGenerationStage.SeaLevel)
            {
                biome = BiomeType.Ocean;
                danger = height < -0.6 ? (byte)2 : (byte)0;
            }
            else if (height > 0.5)
            {
                biome = BiomeType.Mountain;
                danger = 3;
            }
            else if (temperature < 0.18)
            {
                biome = BiomeType.Tundra;
                danger = 1;
            }
            else if (moisture < 0.32 && temperature > 0.55)
            {
                biome = BiomeType.Desert;
                danger = 2;
            }
            else if (temperature > 0.72 && moisture > 0.55)
            {
                biome = BiomeType.Jungle;
                danger = 2;
            }
            else if (moisture > 0.65 && temperature > 0.35)
            {
                biome = BiomeType.Swamp;
                danger = 2;
            }
            else if (moisture > 0.42)
            {
                biome = BiomeType.Forest;
                danger = 1;
            }
            else
            {
                biome = BiomeType.Plains;
                danger = 0;
            }

            // Small deterministic per-tile jitter so neighboring same-biome tiles vary.
            var r = Rng.Create(Seed + 7, i);
            danger = (byte)Math.Min(5, danger + (int)r.NextUInt(2));

            // Per-tile derived seeds: pure function of (Seed, tileIndex), independent of
            // iteration order, matching the on-demand generation doctrine.
            span[i] = new LocalMapInfo
            {
                Seed = Rng.DeriveTileSeed(Seed, i),
                Biome = biome,
                DangerLevel = danger
            };
        }
    }
}
