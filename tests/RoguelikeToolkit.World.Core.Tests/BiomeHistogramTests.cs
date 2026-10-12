using System.Collections.Generic;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Biome-histogram guardrails (backlog E2, E5): the tundra band must exist,
/// glaciers must stay confined to cold ground, and desert counts must not
/// collapse after a glacier-mask retune. Small-matrix slice (sizes 3-4 x
/// seeds 42/7, includes the binding s3seed42 case); the full matrix
/// (sizes 3-6) is probe-verified per retune, see Glaciology.DryPolarGate docs.
/// </summary>
public class BiomeHistogramTests
{
    private static readonly (int Size, int Seed, int DesertPreChange)[] Matrix =
    {
        (3, 42, 10), (3, 7, 16), (4, 42, 23), (4, 7, 56),
    };

    private static Dictionary<BiomeType, int> Histogram(World world)
    {
        var hist = new Dictionary<BiomeType, int>();
        var locals = world.Map.DataStore.GetSpan<LocalMapInfo>();
        for (int i = 0; i < locals.Length; i++)
        {
            var b = locals[i].Biome;
            hist[b] = hist.TryGetValue(b, out int c) ? c + 1 : 1;
        }
        return hist;
    }

    private static int Get(Dictionary<BiomeType, int> hist, BiomeType biome)
        => hist.TryGetValue(biome, out int v) ? v : 0;

    [Fact]
    public void TundraBandExistsOnEveryMatrixBuild()
    {
        foreach (var (size, seed, _) in Matrix)
        {
            using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
            int tundra = Get(Histogram(world), BiomeType.Tundra);
            Assert.True(tundra > 0, $"Size {size} seed {seed}: Tundra extinct");
        }
    }

    [Fact]
    public void GlacierConfinedToColdGroundAndPresent()
    {
        foreach (var (size, seed, _) in Matrix)
        {
            using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
            var store = world.Map.DataStore;
            var locals = store.GetSpan<LocalMapInfo>();
            var climate = store.GetSpan<ClimateInfo>();
            bool hasClimate = store.IsLayerRegistered<ClimateInfo>();
            int glaciers = 0;
            for (int i = 0; i < store.TileCount; i++)
            {
                if (locals[i].Biome != BiomeType.Glacier) continue;
                glaciers++;
                // The mask only fires at or below the snowline (0.15 + 0.11*p,
                // p <= 1) plus noise margin; anything warmer is a mask bug.
                if (hasClimate)
                    Assert.True(climate[i].Temperature <= 0.32,
                        $"Size {size} seed {seed} tile {i}: glacier at temp {climate[i].Temperature}");
            }
            Assert.True(glaciers > 0, $"Size {size} seed {seed}: Glacier extinct");
        }
    }

    [Fact]
    public void DesertCountsDoNotCollapseBelowHalfOfPreChange()
    {
        // The zonal dry belt and the heat-scaled aridity ceiling deliberately grow deserts several
        // fold (the old flat-threshold worlds had almost none), so only the floor is a guardrail.
        foreach (var (size, seed, pre) in Matrix)
        {
            using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
            int desert = Get(Histogram(world), BiomeType.Desert);
            Assert.True(desert >= (int)(pre * 0.5), $"Size {size} seed {seed}: {desert} deserts vs {pre} before");
        }
    }

    [Fact]
    public void DesertsConcentrateInSubtropicalBeltNotAtEquatorOrMidLatitudes()
    {
        int beltLand = 0, beltDesert = 0, otherHotLand = 0, otherHotDesert = 0;
        foreach (var seed in new[] { 42, 7, 1234, 99 })
        {
            using var world = new WorldBuilder().WithSize(5).WithSeed(seed).Build();
            var store = world.Map.DataStore;
            var elev = store.GetSpan<ElevationInfo>();
            var locals = store.GetSpan<LocalMapInfo>();
            var vectors = store.GetTileVectors();
            for (int i = 0; i < store.TileCount; i++)
            {
                if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
                double lat = System.Math.Abs(vectors[i].ToGeoCoord().Latitude);
                bool desert = locals[i].Biome == BiomeType.Desert;
                if (lat >= 15 && lat <= 35) { beltLand++; if (desert) beltDesert++; }
                else if (lat < 50) { otherHotLand++; if (desert) otherHotDesert++; }
            }
        }
        Assert.True(beltLand > 1000 && otherHotLand > 1000, $"samples: belt {beltLand}, other {otherHotLand}");
        double belt = (double)beltDesert / beltLand, other = (double)otherHotDesert / otherHotLand;
        Assert.True(belt > 0.25, $"Subtropical belt desert share {belt:P1} is too small to read as a dry belt");
        Assert.True(belt > 2.5 * other, $"Belt {belt:P1} must clearly exceed the equator and temperate band {other:P1}");
    }

    [Fact]
    public void ZonalFactor_DipsAtSubtropicsAndPeaksAtEquatorAndStormTracks()
    {
        double equator = ClimateStage.ZonalFactor(0), belt = ClimateStage.ZonalFactor(27), storm = ClimateStage.ZonalFactor(55), pole = ClimateStage.ZonalFactor(90);
        Assert.True(equator > 1.2);
        Assert.True(belt < 0.55);
        Assert.True(storm > 1.05);
        Assert.True(pole < 0.9);
        Assert.Equal(1.0, ClimateStage.ZonalFactor(27, 0.0));
        Assert.True(ClimateStage.ZonalFactor(27, 5.0) >= 0.2);
    }

    [Fact]
    public void IsArid_NeedsHeat_AndHeatRaisesTheMoistureCeiling()
    {
        Assert.False(BiomeClassifier.IsArid(0.5, 0.0)); // dry but too cold for desert
        double ceilingHot = BiomeClassifier.DesertMaxMoisture + BiomeClassifier.DesertHeatGain * (1.0 - BiomeClassifier.DesertMinTemperature);
        Assert.True(BiomeClassifier.IsArid(1.0, ceilingHot - 0.01));
        Assert.False(BiomeClassifier.IsArid(1.0, ceilingHot + 0.01));
        Assert.False(BiomeClassifier.IsArid(BiomeClassifier.DesertMinTemperature + 0.01, BiomeClassifier.DesertMaxMoisture + 0.05));
        Assert.Equal(BiomeType.Desert, BiomeClassifier.Classify(0.2, 0.9, 0.35).Biome);
        Assert.NotEqual(BiomeType.Desert, BiomeClassifier.Classify(0.2, 0.6, 0.35).Biome);
    }
}
