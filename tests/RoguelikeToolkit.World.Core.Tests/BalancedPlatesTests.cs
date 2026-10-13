using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Tectonics;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>The balanced-plate preset is the builder default and <c>WithLegacyPlates()</c> opts out: the stage's own defaults must stay legacy, and the preset must deliver what it claims.</summary>
public class BalancedPlatesTests
{
    private static (int[] Area, float[] Continentality, int Land) Run(int seed, Action<WorldBuilder> configure, int size = 4)
    {
        var builder = new WorldBuilder().WithSize(size).WithSeed(seed).WithoutIslandChains();
        configure(builder);
        using var world = builder.Build();
        var store = world.Map.DataStore;
        var plates = store.GetSpan<TectonicPlate>();
        var elev = store.GetSpan<ElevationInfo>();
        var area = new int[13];
        var cont = new float[store.TileCount];
        int land = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            area[plates[i].Id]++;
            cont[i] = plates[i].Continentality;
            if (elev[i].Height >= ElevationGenerationStage.SeaLevel) land++;
        }
        return (area, cont, land);
    }

    [Fact]
    public void WithLegacyPlates_MatchesTheStageOwnDefaults()
    {
        // The stage and partitioner defaults are the legacy generator; the builder preset is what changes them.
        var plain = Run(42, b => b.WithLegacyPlates());
        var explicitDefaults = Run(42, b => b.WithLegacyPlates().WithTectonics(t =>
        {
            t.BalancedSeeding = false;
            t.PlateOffsetWeight = 1.2;
            t.CrustNoiseWeight = 0.55;
            t.CrustWarp = 0;
            t.CrustBlurPasses = 0;
            t.LandFraction = 0;
        }));
        Assert.Equal(plain.Continentality, explicitDefaults.Continentality);
        Assert.Equal(plain.Area, explicitDefaults.Area);
    }

    [Fact]
    public void BalancedPlates_AreDeterministic()
    {
        var a = Run(7, _ => { });
        var b2 = Run(7, _ => { });
        Assert.Equal(a.Continentality, b2.Continentality);
        Assert.Equal(a.Area, b2.Area);
    }

    [Fact]
    public void BalancedPlates_KeepPlateSizesWithinAFewTimesOfEachOther()
    {
        foreach (int seed in new[] { 1, 2, 3, 42 })
        {
            var legacy = Run(seed, b => b.WithLegacyPlates(), size: 5).Area.Skip(1).ToArray();
            var balanced = Run(seed, _ => { }, size: 5).Area.Skip(1).ToArray();
            double legacyRatio = (double)legacy.Max() / Math.Max(1, legacy.Min());
            double balancedRatio = (double)balanced.Max() / Math.Max(1, balanced.Min());
            Assert.True(balanced.Min() > 0, $"seed {seed}: a plate vanished");
            Assert.True(balancedRatio < 8.0, $"seed {seed}: balanced largest/smallest {balancedRatio:F1}");
            Assert.True(balancedRatio < legacyRatio, $"seed {seed}: {balancedRatio:F1} is not tighter than legacy {legacyRatio:F1}");
        }
    }

    [Fact]
    public void BalancedSeeding_SpreadsSeedsAcrossLatitudes()
    {
        // Uniform latitude puts 28% of seeds above 60 degrees; a uniform sphere has 13%. 12 plates x 40 seeds.
        int polar = 0, total = 0;
        foreach (int seed in Enumerable.Range(100, 40))
        {
            using var world = new WorldBuilder().WithSize(3).WithSeed(seed).Build();
            var store = world.Map.DataStore;
            var plates = store.GetSpan<TectonicPlate>();
            var vec = store.GetTileVectors();
            var sum = new Vector3D[13];
            for (int i = 0; i < store.TileCount; i++) sum[plates[i].Id] += vec[i];
            for (int id = 1; id <= 12; id++)
            {
                total++;
                if (Math.Abs(sum[id].Normalize().Z) > Math.Sin(60 * Math.PI / 180)) polar++;
            }
        }
        // Centroids drift toward the equator relative to seeds, so only assert the crowding is not worse than uniform sphere + slack.
        Assert.True(polar / (double)total < 0.20, $"{polar}/{total} plate centroids above 60 degrees");
    }

    [Fact]
    public void LandFraction_SetsTheContinentalShareExactly()
    {
        foreach (double target in new[] { 0.2, 0.3, 0.45 })
        {
            var run = Run(5, b => b.WithTectonics(t => t.LandFraction = target));
            int continental = run.Continentality.Count(c => c > 0.12f);
            double share = continental / (double)run.Continentality.Length;
            Assert.InRange(share, target - 0.01, target + 0.01);
        }
    }

    [Fact]
    public void BalancedPlates_KeepLandShareNearTheLegacyOne()
    {
        double legacy = 0, balanced = 0;
        int[] seeds = { 1, 2, 3, 4, 5, 6 };
        foreach (int seed in seeds)
        {
            var l = Run(seed, bb => bb.WithLegacyPlates(), size: 5);
            var b = Run(seed, _ => { }, size: 5);
            legacy += l.Land / (double)l.Continentality.Length;
            balanced += b.Land / (double)b.Continentality.Length;
        }
        Assert.InRange(balanced / seeds.Length - legacy / seeds.Length, -0.08, 0.08);
    }

    [Fact]
    public void KnobsRejectNonsense()
    {
        var stage = new TectonicPlateGenerationStage();
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.LandFraction = 1.0);
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.LandFraction = -0.1);
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.CrustWarp = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => stage.CrustBlurPasses = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => new NoisyFloodFillPlatePartitioner().SpeedRange = -1);
    }

    [Fact]
    public void BalancedPlates_KeepLegacyRelief()
    {
        // A narrower crust field flattens continents and parks the sea floor near sea level where erosion fills it;
        // CrustContrast restores the spread. Compare the share of land at or above 0.3 and the median land height.
        double LandShareHigh(Func<WorldBuilder, WorldBuilder> configure, out double median)
        {
            var heights = new List<float>();
            // Ten seeds: the legacy share of high land swings between 23% and 38% from seed to seed, so a handful is noise.
            foreach (int seed in new[] { 42, 7, 1, 2, 3, 4, 5, 1234, 99, 100 })
            {
                using var world = configure(new WorldBuilder().WithSize(5).WithSeed(seed)).Build();
                foreach (var e in world.Map.DataStore.GetSpan<ElevationInfo>())
                    if (e.Height >= ElevationGenerationStage.SeaLevel) heights.Add(e.Height);
            }
            heights.Sort();
            median = heights[heights.Count / 2];
            return heights.Count(h => h >= 0.3f) / (double)heights.Count;
        }

        double legacyHigh = LandShareHigh(b => b.WithLegacyPlates(), out double legacyMedian);
        double balancedHigh = LandShareHigh(b => b, out double balancedMedian);
        Assert.InRange(balancedHigh, legacyHigh - 0.08, legacyHigh + 0.08);
        Assert.InRange(balancedMedian, legacyMedian - 0.05, legacyMedian + 0.05);
    }

    [Fact]
    public void WithBalancedPlatesFalse_IsTheSameAsWithLegacyPlates()
    {
        var a = Run(9, b => b.WithBalancedPlates(false));
        var b2 = Run(9, b => b.WithLegacyPlates());
        Assert.Equal(a.Continentality, b2.Continentality);
    }
}
