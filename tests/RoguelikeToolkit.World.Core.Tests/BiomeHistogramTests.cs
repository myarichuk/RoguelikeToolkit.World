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

    private static int HotDesert(Dictionary<BiomeType, int> hist)
    {
        int n = 0;
        foreach (var (biome, count) in hist)
            if (BiomeClassifier.IsDesert(biome) && biome != BiomeType.ColdDesert) n += count;
        return n;
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
            int desert = HotDesert(Histogram(world));
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
                bool desert = BiomeClassifier.IsDesert(locals[i].Biome) && locals[i].Biome != BiomeType.ColdDesert;
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

    [Fact]
    public void AridTypes_SplitBySaltReliefHeightAndHeat()
    {
        const double hot = 0.9, dry = 0.2;
        var flat = new BiomeClassifier.AridSite(0.01, false);
        Assert.Equal(BiomeType.Desert, BiomeClassifier.Classify(0.1, hot, dry, flat).Biome);
        Assert.Equal(BiomeType.RockDesert, BiomeClassifier.Classify(BiomeClassifier.RockDesertHeight + 0.01, hot, dry, flat).Biome);
        Assert.Equal(BiomeType.Badlands, BiomeClassifier.Classify(0.1, hot, dry, new BiomeClassifier.AridSite(BiomeClassifier.BadlandsRelief + 0.01, false)).Biome);
        // Dissection beats elevation, and salt beats both.
        Assert.Equal(BiomeType.Badlands, BiomeClassifier.Classify(0.4, hot, dry, new BiomeClassifier.AridSite(0.3, false)).Biome);
        Assert.Equal(BiomeType.SaltFlat, BiomeClassifier.Classify(0.4, hot, dry, new BiomeClassifier.AridSite(0.3, true)).Biome);
        // A salt site in a wet climate is no salt flat.
        Assert.NotEqual(BiomeType.SaltFlat, BiomeClassifier.Classify(0.1, hot, 0.8, new BiomeClassifier.AridSite(0.0, true)).Biome);
        // The cold side: dry but below hot desert is steppe-desert; wetter or colder is not.
        Assert.Equal(BiomeType.ColdDesert, BiomeClassifier.Classify(0.1, 0.4, 0.15, flat).Biome);
        Assert.NotEqual(BiomeType.ColdDesert, BiomeClassifier.Classify(0.1, 0.4, 0.30, flat).Biome);
        Assert.Equal(BiomeType.Tundra, BiomeClassifier.Classify(0.1, 0.1, 0.05, flat).Biome);
        foreach (var b in new[] { BiomeType.Desert, BiomeType.Badlands, BiomeType.RockDesert, BiomeType.SaltFlat, BiomeType.ColdDesert })
            Assert.True(BiomeClassifier.IsDesert(b));
        Assert.False(BiomeClassifier.IsDesert(BiomeType.Canyon));
    }

    [Fact]
    public void RealWorlds_ShowEveryDesertType_AndNoSingleTypeOwnsThem()
    {
        var total = new Dictionary<BiomeType, int>();
        foreach (var seed in new[] { 42, 7, 1234, 99 })
        {
            using var world = new WorldBuilder().WithSize(5).WithSeed(seed).Build();
            foreach (var (biome, count) in Histogram(world))
                if (BiomeClassifier.IsDesert(biome)) total[biome] = Get(total, biome) + count;
        }
        int all = 0;
        foreach (var n in total.Values) all += n;
        foreach (var b in new[] { BiomeType.Desert, BiomeType.Badlands, BiomeType.RockDesert, BiomeType.SaltFlat, BiomeType.ColdDesert })
            Assert.True(Get(total, b) >= 40, $"{b}: only {Get(total, b)} of {all} desert tiles");
        foreach (var (biome, count) in total)
            Assert.True(count < all * 0.7, $"{biome} is {count} of {all} desert tiles; the family collapsed into one type");
    }

    [Fact]
    public void SaltFlats_SitOnSalineGroundFromTheLakeSolver()
    {
        foreach (var seed in new[] { 42, 7, 1234, 99 })
        {
            using var world = new WorldBuilder().WithSize(5).WithSeed(seed).Build();
            var store = world.Map.DataStore;
            var locals = store.GetSpan<LocalMapInfo>();
            var hydro = store.GetSpan<HydrologyInfo>();
            var climate = store.GetSpan<ClimateInfo>();
            for (int i = 0; i < store.TileCount; i++)
            {
                if (locals[i].Biome != BiomeType.SaltFlat) continue;
                Assert.True(hydro[i].Saline == 1, $"seed {seed} tile {i}: salt flat the lake solver did not mark saline");
                Assert.True(hydro[i].LakeDepth <= 0f, $"seed {seed} tile {i}: salt flat under open water");
                Assert.True(climate[i].Precipitation < BiomeClassifier.PlayaMaxMoisture, $"seed {seed} tile {i}: salt flat in a wet climate");
            }
        }
    }

    [Fact]
    public void Smoothing_MergesHotAridSpeckle_ButNeverCrossesFamilies()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(5).Build();
        var store = world.Map.DataStore;
        var span = store.GetSpan<LocalMapInfo>();
        var neighbors = new int[6];
        int centre = 100;
        int n = store.GetAdjacent(centre, neighbors);
        for (int i = 0; i < span.Length; i++) span[i] = new LocalMapInfo { Biome = BiomeType.Ocean, DangerLevel = 0 };
        span[centre] = new LocalMapInfo { Biome = BiomeType.Desert, DangerLevel = 2 };
        for (int k = 0; k < n; k++) span[neighbors[k]] = new LocalMapInfo { Biome = BiomeType.Badlands, DangerLevel = 3 };
        LocalMapGenerationStage.SmoothMoistureBiomes(store, span, 1);
        Assert.Equal(BiomeType.Badlands, span[centre].Biome);
        Assert.Equal(3, span[centre].DangerLevel); // re-based from the desert's 2 to badlands' 3

        // A sand tile ringed by plains stays sand: the families never mix.
        span[centre] = new LocalMapInfo { Biome = BiomeType.Desert, DangerLevel = 2 };
        for (int k = 0; k < n; k++) span[neighbors[k]] = new LocalMapInfo { Biome = BiomeType.Plains, DangerLevel = 0 };
        LocalMapGenerationStage.SmoothMoistureBiomes(store, span, 1);
        Assert.Equal(BiomeType.Desert, span[centre].Biome);

        // Salt flats and cold desert are never smoothed away.
        span[centre] = new LocalMapInfo { Biome = BiomeType.SaltFlat, DangerLevel = 2 };
        for (int k = 0; k < n; k++) span[neighbors[k]] = new LocalMapInfo { Biome = BiomeType.Desert, DangerLevel = 2 };
        LocalMapGenerationStage.SmoothMoistureBiomes(store, span, 1);
        Assert.Equal(BiomeType.SaltFlat, span[centre].Biome);
    }

    [Fact]
    public void Savanna_NeedsHeat_SeasonalRain_AndStopsWhereForestStarts()
    {
        const double warm = 0.8, mid = 0.5, seasonal = 0.9;
        var flat = new BiomeClassifier.AridSite(0.01, false);
        Assert.Equal(BiomeType.Savanna, BiomeClassifier.Classify(0.1, warm, mid, flat, seasonal).Biome);
        // The same climate with even rain is the old ladder's answer: no seasonal data, no savanna.
        Assert.Equal(BiomeType.Forest, BiomeClassifier.Classify(0.1, warm, mid, flat, 0.0).Biome);
        Assert.Equal(BiomeType.Forest, BiomeClassifier.Classify(0.1, warm, mid, flat).Biome);
        Assert.Equal(BiomeType.Forest, BiomeClassifier.Classify(0.1, warm, mid, flat, BiomeClassifier.SavannaMinSeasonality - 0.01).Biome);
        // Too cool for a dry season to be a drought, too wet for grass, too dry for anything but desert.
        Assert.NotEqual(BiomeType.Savanna, BiomeClassifier.Classify(0.1, BiomeClassifier.SavannaMinTemperature - 0.05, mid, flat, seasonal).Biome);
        Assert.Equal(BiomeType.Jungle, BiomeClassifier.Classify(0.1, 0.9, 0.7, flat, seasonal).Biome);
        Assert.Equal(BiomeType.Swamp, BiomeClassifier.Classify(0.1, 0.65, 0.7, flat, seasonal).Biome);
        Assert.Equal(BiomeType.Desert, BiomeClassifier.Classify(0.1, 0.9, 0.2, flat, seasonal).Biome);
        // Water, high ground and ice outrank it.
        Assert.Equal(BiomeType.Ocean, BiomeClassifier.Classify(-0.2, warm, mid, flat, seasonal).Biome);
        Assert.Equal(BiomeType.Mountain, BiomeClassifier.Classify(0.8, warm, mid, flat, seasonal).Biome);
        // The danger every consumer re-bases on matches the ladder's.
        Assert.Equal(BiomeClassifier.BaseDanger(BiomeType.Savanna), BiomeClassifier.Classify(0.1, warm, mid, flat, seasonal).Danger);
    }

    [Fact]
    public void SavannaExistsOnEveryMatrixBuild_MostlyInWarmSeasonalNonAridCountry()
    {
        foreach (var (size, seed, _) in Matrix)
        {
            using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
            var locals = world.Map.DataStore.GetSpan<LocalMapInfo>();
            var climate = world.Map.DataStore.GetSpan<ClimateInfo>();
            int savanna = 0, qualifying = 0;
            for (int i = 0; i < locals.Length; i++)
            {
                if (locals[i].Biome != BiomeType.Savanna) continue;
                savanna++;
                if (BiomeClassifier.IsSavanna(climate[i].Temperature, climate[i].Precipitation, climate[i].RainSeasonality)) qualifying++;
            }
            // Neighbour smoothing may flip a stray plains tile into a savanna block, so not every tile qualifies on its own.
            Assert.True(qualifying >= savanna * 0.85, $"Size {size} seed {seed}: only {qualifying} of {savanna} savanna tiles meet the rule");
            Assert.True(savanna > 0, $"Size {size} seed {seed}: no savanna");
        }
    }

    [Fact]
    public void Smoothing_TreatsSavannaAsPartOfTheMoistureFamily()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(1).Build();
        var store = world.Map.DataStore;
        var span = new LocalMapInfo[store.TileCount];
        for (int i = 0; i < span.Length; i++) span[i] = new LocalMapInfo { Biome = BiomeType.Savanna, DangerLevel = 1 };
        int centre = 0;
        span[centre] = new LocalMapInfo { Biome = BiomeType.Plains, DangerLevel = 0 };
        LocalMapGenerationStage.SmoothMoistureBiomes(store, span, 1);
        Assert.Equal(BiomeType.Savanna, span[centre].Biome);
        Assert.Equal(1, span[centre].DangerLevel);
    }
}
