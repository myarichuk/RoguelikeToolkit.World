using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Climate must behave like a planet: moist air comes off the ocean and dries out as it travels
/// inland, so aridity belongs to continental interiors and rain shadows, not to the shoreline.
/// Before moisture transport, precipitation was noise plus a flat +0.06 at the coast, and deserts
/// were as likely on the shore as in a continent's heart.
/// </summary>
public class ClimateContinentalityTests
{
    /// <summary>Rings from the nearest sea-level-or-lower tile; 0 = water.</summary>
    private static int[] CoastDistance(WorldDataStore store, Span<ElevationInfo> elev)
    {
        int n = store.TileCount;
        var dist = new int[n];
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) { dist[i] = 0; queue.Enqueue(i); }
            else dist[i] = -1;
        }
        Span<int> nb = stackalloc int[6];
        while (queue.Count > 0)
        {
            int t = queue.Dequeue();
            int a = store.GetAdjacent(t, nb);
            for (int k = 0; k < a; k++)
                if (dist[nb[k]] < 0) { dist[nb[k]] = dist[t] + 1; queue.Enqueue(nb[k]); }
        }
        return dist;
    }

    [Fact]
    public void Coasts_AreWetterAndLessAridThanInteriors()
    {
        // Only the stages that decide climate run (tectonics, elevation, climate). Erosion comes
        // after climate and moves the shoreline, so measuring coast distance on the final heights
        // would compare tiles against a coast the moisture transport never saw. Pooled over seeds:
        // any single landmass can be a thin archipelago with no interior at all.
        double coastP = 0, inlandP = 0;
        int coastN = 0, inlandN = 0, coastArid = 0, inlandArid = 0;
        foreach (var (size, seed) in new[] { (5, 42), (5, 7), (5, 1234), (6, 42) })
        {
            var builder = new WorldBuilder().WithSize(size).WithSeed(seed).WithoutDefaultStages();
            builder.AddStage(new TectonicPlateGenerationStage(12, seed));
            builder.AddStage(new ElevationGenerationStage(seed));
            builder.AddStage(new ClimateStage(seed));
            using var world = builder.Build();
            var store = world.Map.DataStore;
            var elev = store.GetSpan<ElevationInfo>();
            var climate = store.GetSpan<ClimateInfo>();
            var dist = CoastDistance(store, elev);
            // Rings are tile hops; scale the "interior" cut with resolution (size 5 ~ 10k tiles).
            int inland = size >= 6 ? 12 : 6;
            for (int i = 0; i < store.TileCount; i++)
            {
                // Hot ground only: a desert needs heat, so cold or temperate tiles cannot dilute the share.
                if (dist[i] <= 0 || climate[i].Temperature <= BiomeClassifier.DesertMinTemperature) continue;
                bool arid = BiomeClassifier.IsArid(climate[i].Temperature, climate[i].Precipitation);
                if (dist[i] <= 2) { coastN++; coastP += climate[i].Precipitation; if (arid) coastArid++; }
                else if (dist[i] >= inland) { inlandN++; inlandP += climate[i].Precipitation; if (arid) inlandArid++; }
            }
        }
        Assert.True(coastN > 500 && inlandN > 500, $"Not enough samples (coast {coastN}, inland {inlandN})");
        Assert.True(coastP / coastN - inlandP / inlandN > 0.10,
            $"Coastal precipitation {coastP / coastN:F3} should clearly exceed interior {inlandP / inlandN:F3}");
        Assert.True((double)coastArid / coastN < (double)inlandArid / inlandN,
            $"Arid share on a hot shore ({coastArid}/{coastN}) must stay below the interior's ({inlandArid}/{inlandN})");
    }

    [Fact]
    public void MoistureTransport_IsDeterministic()
    {
        using var a = new WorldBuilder().WithSize(4).WithSeed(11).Build();
        using var b = new WorldBuilder().WithSize(4).WithSeed(11).Build();
        var ca = a.Map.DataStore.GetSpan<ClimateInfo>();
        var cb = b.Map.DataStore.GetSpan<ClimateInfo>();
        for (int i = 0; i < ca.Length; i++) Assert.Equal(ca[i].Precipitation, cb[i].Precipitation);
    }
}
