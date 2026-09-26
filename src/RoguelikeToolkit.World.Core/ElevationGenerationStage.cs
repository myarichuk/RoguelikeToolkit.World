using System;

namespace RoguelikeToolkit.World.Core;

[WorldGeneratorStage(15, Reads = new[] { typeof(TectonicPlate) }, Writes = new[] { typeof(ElevationInfo) })]
public class ElevationGenerationStage : IWorldGeneratorStage, ISeededStage
{
    public const double SeaLevel = 0.0;

    public int Seed { get; set; } = 42;
    public double NoiseAmplitude { get; set; } = 0.55;
    public double NoiseFrequency { get; set; } = 3.0;
    public double BoundaryUplift { get; set; } = 0.85;

    /// <summary>Rolling-hill amplitude on land (abyssal texture is ~40% of this).</summary>
    public double HillAmplitude { get; set; } = 0.085;

    /// <summary>Ridged-crest amplitude per unit of positive orogeny.</summary>
    public double RidgeAmplitude { get; set; } = 0.38;

    /// <summary>Hotspot island-chain count (deterministic positions from seed).</summary>
    public int HotspotCount { get; set; } = 3;

    /// <summary>Hotspot swell height; oceanic hotspots breach as volcanic islands.</summary>
    public double HotspotAmplitude { get; set; } = 0.55;

    public ElevationGenerationStage()
    {
    }

    public ElevationGenerationStage(int seed = 42)
    {
        Seed = seed;
    }

    public void Execute(WorldMap map)
    {
        var store = map.DataStore;
        var plates = store.GetSpan<TectonicPlate>();
        var elev = store.GetSpan<ElevationInfo>();
        var vectors = store.GetTileVectors();

        // Deterministic hotspot anchors (mantle plumes, independent of plates).
        var hotspots = new Vector3D[Math.Max(0, HotspotCount)];
        for (int h = 0; h < hotspots.Length; h++)
        {
            var r = Rng.Create(Seed + 9000, h);
            double lat = r.NextDouble() * 180.0 - 90.0;
            double lon = r.NextDouble() * 360.0 - 180.0;
            hotspots[h] = Vector3D.FromGeoCoord(new GeoCoord(lat, lon));
        }

        for (int i = 0; i < store.TileCount; i++)
        {
            var plate = plates[i];
            var v = vectors[i];

            // Base hypsometry from continentality: continents ride high, ocean
            // basins low, with broad swells + rolling hills + micro ripples.
            bool land = plate.Continentality > 0;
            double height = plate.Continentality * 0.85;
            height += SphereNoise.Fbm(v * 2.3, Seed + 7) * 0.20;
            height += SphereNoise.Fbm(v * 6.5, Seed + 71) * HillAmplitude * (land ? 1.0 : 0.4);
            height += SphereNoise.Fbm(v * 15.0, Seed + 717) * 0.03;

            // Legacy broad relief term (kept for continuity of the height range).
            height += SphereNoise.Fbm(v * NoiseFrequency, Seed) * NoiseAmplitude * 0.15;

            // Orogeny: signed belt driver already decayed by boundary distance
            // (fold belts / arcs uplift, trenches and rifts depress).
            double orogeny = plate.Orogeny * BoundaryUplift;
            height += Math.Min(Math.Max(orogeny, -1.0), 2.0);

            // Ridged crests inside uplift belts (sharp ranges, wide plateaus).
            if (plate.Orogeny > 0.02 && plate.Continentality > -0.2)
            {
                double ridge = SphereNoise.RidgedFbm(v * 5.0, Seed + 313) - 0.55;
                height += ridge * Math.Min(plate.Orogeny, 1.5) * RidgeAmplitude;
            }

            // Hotspot swells: gaussian domes that breach as islands/seamounts.
            for (int h = 0; h < hotspots.Length; h++)
            {
                double dot = Math.Clamp(Vector3D.Dot(v, hotspots[h]), -1.0, 1.0);
                double ang = Math.Acos(dot);
                double swell = Math.Exp(-(ang * ang) / (2.0 * 0.055 * 0.055));
                if (swell > 0.01) height += swell * HotspotAmplitude;
            }

            // Oceanic crust sits lower on average (abyssal plains).
            if (plate.Crust == CrustType.Oceanic) height -= 0.10;
            elev[i] = new ElevationInfo { Height = (float)height };
        }
    }
}
