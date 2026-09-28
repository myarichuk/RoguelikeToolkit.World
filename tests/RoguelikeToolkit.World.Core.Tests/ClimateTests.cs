using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class ClimateTests
{
    [Fact]
    public void ClimateStage_ProducesBoundedFields()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(42).Build();
        var store = world.Map.DataStore;
        var climate = store.GetSpan<ClimateInfo>();

        for (int i = 0; i < store.TileCount; i++)
        {
            Assert.InRange(climate[i].Temperature, 0f, 1f);
            Assert.InRange(climate[i].Precipitation, 0f, 1f);
            double windLen = Math.Sqrt(climate[i].WindX * climate[i].WindX +
                climate[i].WindY * climate[i].WindY + climate[i].WindZ * climate[i].WindZ);
            Assert.True(windLen > 0.5, $"Tile {i} has degenerate wind");
        }
    }

    [Fact]
    public void OrographicEffect_LeewardMeanDrierThanWindward()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(7).Build();
        var store = world.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        var vectors = store.GetTileVectors();
        Span<int> neighbors = stackalloc int[6];

        double leeSum = 0, windSum = 0;
        int leeCount = 0, windCount = 0;

        for (int i = 0; i < store.TileCount; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            var wind = new Vector3D(climate[i].WindX, climate[i].WindY, climate[i].WindZ);
            int adjacent = store.GetAdjacent(i, neighbors);
            int upwind = -1;
            double bestUp = double.NegativeInfinity;
            for (int k = 0; k < adjacent; k++)
            {
                int j = neighbors[k];
                var tang = (vectors[j] - vectors[i]).Normalize();
                double up = -Vector3D.Dot(tang, wind);
                if (up > bestUp) { bestUp = up; upwind = j; }
            }
            if (upwind < 0 || bestUp <= 0.15) continue;
            double dh = elev[i].Height - elev[upwind].Height;
            if (dh > 0.08) { windSum += climate[i].Precipitation; windCount++; }
            else if (dh < -0.08) { leeSum += climate[i].Precipitation; leeCount++; }
        }

        Assert.True(windCount > 0 && leeCount > 0,
            $"Need both faces for a rain-shadow check (windward={windCount}, leeward={leeCount})");
        Assert.True(leeSum / leeCount < windSum / windCount,
            $"Leeward mean {leeSum / leeCount:F3} not drier than windward {windSum / windCount:F3}");
    }

    [Fact]
    public void RegisteredButUnrunHydro_MatchesUnregisteredMoistureField()
    {
        var withoutHydro = RunClimateToPrecip(registerHydro: false);
        var withUnrunHydro = RunClimateToPrecip(registerHydro: true);
        Assert.Equal(withoutHydro.Length, withUnrunHydro.Length);
        for (int i = 0; i < withoutHydro.Length; i++)
            Assert.Equal(withoutHydro[i], withUnrunHydro[i]);
    }

    private static float[] RunClimateToPrecip(bool registerHydro)
    {
        using var map = new WorldMap(2);
        var plates = new TectonicPlateLayer(map.DataStore, 12, seed: 42);
        map.RegisterLayer(plates);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        if (registerHydro)
            map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.DataStore.Allocate();

        new TectonicPlateGenerationStage(12, 42).Execute(map);
        new ElevationGenerationStage(42).Execute(map);
        new ClimateStage(42).Execute(map);

        var span = map.DataStore.GetSpan<ClimateInfo>();
        var precip = new float[span.Length];
        for (int i = 0; i < span.Length; i++) precip[i] = span[i].Precipitation;
        return precip;
    }

    [Fact]
    public void Climate_IsDeterministicForSameSeed()
    {
        using var first = new WorldBuilder().WithSize(2).WithSeed(9).Build();
        using var second = new WorldBuilder().WithSize(2).WithSeed(9).Build();
        var a = first.Map.DataStore.GetSpan<ClimateInfo>();
        var b = second.Map.DataStore.GetSpan<ClimateInfo>();
        for (int i = 0; i < a.Length; i++)
        {
            Assert.Equal(a[i].Temperature, b[i].Temperature);
            Assert.Equal(a[i].Precipitation, b[i].Precipitation);
        }
    }
}
