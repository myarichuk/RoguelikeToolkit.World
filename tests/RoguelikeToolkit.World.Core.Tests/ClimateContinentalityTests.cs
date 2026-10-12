using System;
using System.Collections.Generic;
using RoguelikeToolkit.World.Core;
using Xunit;

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
        // Warm lowland only, so latitude and altitude do not confound the comparison. Pooled over
        // seeds: any single landmass can be a thin archipelago with no interior at all.
        double coastP = 0, inlandP = 0;
        int coastN = 0, inlandN = 0, coastDesert = 0, inlandDesert = 0;
        foreach (var (size, seed) in new[] { (5, 42), (5, 7), (5, 1234), (6, 42) })
        {
            using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
            var store = world.Map.DataStore;
            var elev = store.GetSpan<ElevationInfo>();
            var climate = store.GetSpan<ClimateInfo>();
            var local = store.GetSpan<LocalMapInfo>();
            var dist = CoastDistance(store, elev);
            // Rings are tile hops; scale the "interior" cut with resolution (size 5 ~ 10k tiles).
            int inland = size >= 6 ? 12 : 6;
            for (int i = 0; i < store.TileCount; i++)
            {
                if (dist[i] <= 0 || climate[i].Temperature < 0.4f || elev[i].Height > BiomeClassifier.MountainHeight) continue;
                bool desert = local[i].Biome == BiomeType.Desert;
                if (dist[i] <= 2) { coastN++; coastP += climate[i].Precipitation; if (desert) coastDesert++; }
                else if (dist[i] >= inland) { inlandN++; inlandP += climate[i].Precipitation; if (desert) inlandDesert++; }
            }
        }
        Assert.True(coastN > 500 && inlandN > 500, $"Not enough samples (coast {coastN}, inland {inlandN})");
        Assert.True(coastP / coastN - inlandP / inlandN > 0.10,
            $"Coastal precipitation {coastP / coastN:F3} should clearly exceed interior {inlandP / inlandN:F3}");
        Assert.True((double)coastDesert / coastN * 2 < (double)inlandDesert / inlandN,
            $"Desert share on the shore ({coastDesert}/{coastN}) must be under half the interior's ({inlandDesert}/{inlandN})");
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
