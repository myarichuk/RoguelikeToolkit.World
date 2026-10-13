using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Land use is a decision layer on top of the physics: capacity says what a tile could support, plugin stages
/// decide, hand-painted pins win, and nothing here may feed back into climate, rivers or biomes.
/// </summary>
public class LandUseTests
{
    private const int Size = 3, Seed = 42;

    private static LandSite Site(BiomeType biome = BiomeType.Plains, double height = 0.1, double t = 0.6, double p = 0.5,
        double slope = 0.3, bool fresh = false, bool onFresh = false, double heat = 0, bool coast = true)
        => new(biome, height, t, p, slope, fresh, onFresh, false, heat, coast);

    // ---- the capacity model ----------------------------------------------------------------

    [Fact]
    public void Arable_FollowsWarmthRainAndFlatness()
    {
        var good = LandCapacity.Evaluate(Site()).Arable;
        Assert.True(good > 150, $"temperate plains should be good farmland, got {good}");
        Assert.True(LandCapacity.Evaluate(Site(t: 0.1)).Arable < good / 4, "too cold to farm");
        Assert.True(LandCapacity.Evaluate(Site(p: 0.1)).Arable < good / 4, "too dry to farm");
        Assert.True(LandCapacity.Evaluate(Site(slope: 5.0)).Arable < good / 4, "too steep to farm");
        Assert.True(LandCapacity.Evaluate(Site(height: 0.55)).Arable < good / 4, "too high to farm");
    }

    [Fact]
    public void ARiverMakesTheDifferenceInADesert()
    {
        var dry = Site(BiomeType.Desert, p: 0.1, t: 0.8);
        Assert.Equal(0, LandCapacity.Evaluate(dry).Arable);
        var delta = LandCapacity.Evaluate(dry with { FreshWater = true });
        Assert.True(delta.Arable > 60, $"an irrigated delta should be farmable, got {delta.Arable}");
    }

    [Theory]
    [InlineData(BiomeType.Glacier)]
    [InlineData(BiomeType.SaltFlat)]
    [InlineData(BiomeType.Canyon)]
    public void BarrenGroundYieldsNoTillageOrTimber(BiomeType biome)
    {
        var c = LandCapacity.Evaluate(Site(biome));
        Assert.Equal(0, c.Arable);
        Assert.Equal(0, c.Timber);
    }

    [Fact]
    public void ForestIsTheTimberAndGameBiome_PlainsTheGrazingOne()
    {
        var forest = LandCapacity.Evaluate(Site(BiomeType.Forest, p: 0.6));
        var plains = LandCapacity.Evaluate(Site(BiomeType.Plains, p: 0.6));
        Assert.True(forest.Timber > plains.Timber * 3);
        Assert.True(forest.Game > plains.Game);
        Assert.True(plains.Pasture > forest.Pasture);
        Assert.True(plains.Arable > forest.Arable, "forest has to be cleared first");
    }

    [Fact]
    public void SteppeGrazesWhereItCannotBeFarmed()
    {
        var steppe = LandCapacity.Evaluate(Site(BiomeType.ColdDesert, t: 0.3, p: 0.15));
        Assert.True(steppe.Pasture > 0 && steppe.Pasture > steppe.Arable);
    }

    [Fact]
    public void LandTilesCatchNoFish_OceanTilesCatchOnlyFish()
    {
        var land = LandCapacity.Evaluate(Site());
        Assert.Equal(0, land.Fish);
        var sea = LandCapacity.Evaluate(Site(BiomeType.Ocean, height: -0.1, t: 0.5));
        Assert.True(sea.Fish > 0);
        Assert.Equal(0, sea.Arable + sea.Pasture + sea.Game + sea.Timber);
    }

    [Fact]
    public void FishFavourShelvesColdUpwellingAndTheCoast()
    {
        var shelf = LandCapacity.Evaluate(Site(BiomeType.Ocean, height: -0.05)).Fish;
        var deep = LandCapacity.Evaluate(Site(BiomeType.Ocean, height: -0.8)).Fish;
        Assert.True(shelf > deep * 4, $"shelf {shelf} vs deep {deep}");
        var upwelling = LandCapacity.Evaluate(Site(BiomeType.Ocean, height: -0.05, heat: -LandCapacity.UpwellingAnomaly)).Fish;
        Assert.True(upwelling > shelf, $"upwelling {upwelling} vs plain shelf {shelf}");
        var openWater = LandCapacity.Evaluate(Site(BiomeType.Ocean, height: -0.05, coast: false)).Fish;
        Assert.True(openWater < shelf);
        var tropical = LandCapacity.Evaluate(Site(BiomeType.Ocean, height: -0.05, t: 0.95)).Fish;
        Assert.True(tropical < shelf);
    }

    [Fact]
    public void FreshWaterTilesAreFishable_EvenInDesert()
    {
        var river = LandCapacity.Evaluate(Site(BiomeType.Desert, t: 0.8, p: 0.1, fresh: true, onFresh: true));
        Assert.True(river.Fish > 0);
        Assert.Equal(0, LandCapacity.Evaluate(Site(BiomeType.Desert, t: 0.8, p: 0.1)).Fish);
    }

    [Fact]
    public void Capacity_IsSmooth_NoCliffsAcrossAThreshold()
    {
        // A hard cut-off would stripe the planet into tile speckle: stepping an input a hair must move the output a hair.
        for (double p = 0.0; p < 1.0; p += 0.001)
        {
            int a = LandCapacity.Evaluate(Site(p: p)).Arable, b = LandCapacity.Evaluate(Site(p: p + 0.001)).Arable;
            Assert.True(Math.Abs(a - b) <= 4, $"arable jumped {a}->{b} at p={p}");
        }
    }

    // ---- the layers in a world ---------------------------------------------------------------

    private static World Build(Action<WorldBuilder>? configure = null)
    {
        var b = new WorldBuilder().WithSize(Size).WithSeed(Seed).WithLandUse();
        configure?.Invoke(b);
        return b.Build();
    }

    [Fact]
    public void LandUse_CannotMoveThePhysics()
    {
        using var plain = new WorldBuilder().WithSize(Size).WithSeed(Seed).WithUserEdits().Build();
        using var land = Build();
        var pe = plain.Map.DataStore.GetSpan<ElevationInfo>(); var pc = plain.Map.DataStore.GetSpan<ClimateInfo>();
        var pl = plain.Map.DataStore.GetSpan<LocalMapInfo>(); var ph = plain.Map.DataStore.GetSpan<HydrologyInfo>();
        var le = land.Map.DataStore.GetSpan<ElevationInfo>(); var lc = land.Map.DataStore.GetSpan<ClimateInfo>();
        var ll = land.Map.DataStore.GetSpan<LocalMapInfo>(); var lh = land.Map.DataStore.GetSpan<HydrologyInfo>();
        for (int i = 0; i < pe.Length; i++)
        {
            Assert.Equal(pe[i].Height, le[i].Height);
            Assert.Equal(pc[i].Temperature, lc[i].Temperature);
            Assert.Equal(pc[i].Precipitation, lc[i].Precipitation);
            Assert.Equal(pl[i].Biome, ll[i].Biome);
            Assert.Equal(ph[i].Flow, lh[i].Flow);
        }
        Assert.Equal(plain.Rivers.Rivers.Count, land.Rivers.Rivers.Count);
    }

    [Fact]
    public void ByDefault_NobodyHasDecidedAnything_AndCapacityTracksTheBiome()
    {
        using var w = Build();
        var s = w.Map.DataStore;
        var use = s.GetSpan<LandUseInfo>();
        var cap = s.GetSpan<LandCapacityInfo>();
        var biome = s.GetSpan<LocalMapInfo>();
        Assert.All(use.ToArray(), u => Assert.Equal(default, u));

        int arable = 0, fish = 0, oceanWithLandCapacity = 0;
        for (int i = 0; i < cap.Length; i++)
        {
            if (biome[i].Biome == BiomeType.Ocean)
            {
                if (cap[i].Fish > 0) fish++;
                if (cap[i].Arable + cap[i].Pasture + cap[i].Game + cap[i].Timber != 0) oceanWithLandCapacity++;
            }
            else if (cap[i].Arable > 100) arable++;
        }
        Assert.Equal(0, oceanWithLandCapacity);
        Assert.True(arable > 0, "a size-3 Earth should have some good farmland");
        Assert.True(fish > 0, "and some fishing water");
    }

    [Fact]
    public void Capacity_IsDeterministic()
    {
        using var a = Build(); using var b = Build();
        Assert.Equal(a.Map.DataStore.GetSpan<LandCapacityInfo>().ToArray(), b.Map.DataStore.GetSpan<LandCapacityInfo>().ToArray());
    }

    [Fact]
    public void ColdCurrentsRaiseTheFishingOffTheirCoast()
    {
        // The Humboldt/Benguela effect, through the real pipeline: ocean tiles with a cold anomaly out-fish the warm ones.
        using var w = Build();
        var s = w.Map.DataStore;
        var cap = s.GetSpan<LandCapacityInfo>(); var cl = s.GetSpan<ClimateInfo>(); var loc = s.GetSpan<LocalMapInfo>();
        double cold = 0, warm = 0; int nc = 0, nw = 0;
        for (int i = 0; i < cap.Length; i++)
        {
            if (loc[i].Biome != BiomeType.Ocean || cap[i].Fish == 0) continue;
            if (cl[i].OceanHeat < -0.01f) { cold += cap[i].Fish; nc++; }
            else if (cl[i].OceanHeat > 0.01f) { warm += cap[i].Fish; nw++; }
        }
        Assert.True(nc > 0 && nw > 0, $"need both anomalies on the map (cold {nc}, warm {nw})");
        Assert.True(cold / nc > warm / nw, $"cold {cold / nc:F1} vs warm {warm / nw:F1}");
    }

    // ---- the plugin contract -----------------------------------------------------------------

    /// <summary>A stand-in for a plugin: farm the good ground, hunt the forests.</summary>
    [WorldGeneratorStage(30, Reads = new[] { typeof(LandCapacityInfo), typeof(LandUseInfo) }, Writes = new[] { typeof(LandUseInfo) })]
    private sealed class FarmEverythingGood : IWorldGeneratorStage
    {
        public void Execute(WorldMap map)
        {
            var cap = map.DataStore.GetSpan<LandCapacityInfo>();
            var use = map.DataStore.GetSpan<LandUseInfo>();
            for (int i = 0; i < use.Length; i++)
            {
                if (cap[i].Arable >= 128) use[i] = new LandUseInfo { Use = LandUseId.Farmland, Intensity = cap[i].Arable };
                if (cap[i].Game >= 128) use[i].Activities |= (byte)LandActivity.Hunting;
            }
        }
    }

    [WorldGeneratorStage(31, Reads = new[] { typeof(LandCapacityInfo) }, Writes = new[] { typeof(LandUseInfo) })]
    private sealed class BlindWriter : IWorldGeneratorStage
    {
        public void Execute(WorldMap map) { }
    }

    [Fact]
    public void APluginAllocatesFromCapacity_AndTheResultIsDeterministic()
    {
        using var a = Build(b => b.AddStage(new FarmEverythingGood()));
        using var b2 = Build(b => b.AddStage(new FarmEverythingGood()));
        var use = a.Map.DataStore.GetSpan<LandUseInfo>();
        var cap = a.Map.DataStore.GetSpan<LandCapacityInfo>();
        int farm = 0, hunted = 0;
        for (int i = 0; i < use.Length; i++)
        {
            Assert.Equal(cap[i].Arable >= 128, use[i].Use == LandUseId.Farmland);
            if (use[i].Use == LandUseId.Farmland) farm++;
            if ((use[i].Activities & (byte)LandActivity.Hunting) != 0) hunted++;
        }
        Assert.True(farm > 0 && hunted > 0, $"farm {farm}, hunted {hunted}");
        Assert.Equal(use.ToArray(), b2.Map.DataStore.GetSpan<LandUseInfo>().ToArray());
    }

    [Fact]
    public void ABlindLandUseWriter_IsRejectedByTheContract()
    {
        // Stage 25 is the first writer; a later writer that does not read the layer would silently discard it.
        var ex = Assert.ThrowsAny<Exception>(() => Build(b => b.AddStage(new BlindWriter())));
        Assert.Contains("blind", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ---- hand-painted pins -------------------------------------------------------------------

    private static int AnyTile(World w, Func<int, bool> pred)
    {
        for (int i = 0; i < w.Map.DataStore.TileCount; i++) if (pred(i)) return i;
        throw new InvalidOperationException("no such tile");
    }

    [Fact]
    public void APin_WinsOverThePlugin_AndRemovingItRestoresThePluginsAnswer()
    {
        using var w = Build(b => b.AddStage(new FarmEverythingGood()));
        var useArr = w.Map.DataStore.GetSpan<LandUseInfo>().ToArray();
        var capArr = w.Map.DataStore.GetSpan<LandCapacityInfo>().ToArray();
        int farm = AnyTile(w, i => capArr[i].Arable >= 128);
        int bare = AnyTile(w, i => capArr[i].Arable == 0 && useArr[i].Use == 0);
        var before = useArr;

        w.Edits[farm].Flags |= UserEditFlags.PinLandUse;                               // pin "nothing" over a plugin's farm
        w.Edits[bare].Flags |= UserEditFlags.PinLandUse; w.Edits[bare].LandUse = LandUseId.Ashlands;
        w.Edits[bare].LandActivities = (byte)LandActivity.Grazing;
        w.Recompute();
        var use = w.Map.DataStore.GetSpan<LandUseInfo>();
        Assert.Equal(default, use[farm]);
        Assert.Equal(LandUseId.Ashlands, use[bare].Use);
        Assert.Equal((byte)LandActivity.Grazing, use[bare].Activities);
        Assert.Equal(255, use[bare].Intensity);

        w.Edits[farm] = default; w.Edits[bare] = default;
        w.Recompute();
        Assert.Equal(before, w.Map.DataStore.GetSpan<LandUseInfo>().ToArray());       // a clean slate, nothing stale
    }

    [Fact]
    public void LandUsePins_DoNotChangeThePhysics()
    {
        using var w = Build();
        var snap = w.Map.DataStore.GetSpan<LocalMapInfo>().ToArray();
        var flow = w.Map.DataStore.GetSpan<HydrologyInfo>().ToArray().Select(h => h.Flow).ToArray();
        int t = AnyTile(w, i => snap[i].Biome == BiomeType.Plains);
        w.Edits[t].Flags |= UserEditFlags.PinLandUse; w.Edits[t].LandUse = LandUseId.Farmland;
        w.Recompute();
        Assert.Equal(snap, w.Map.DataStore.GetSpan<LocalMapInfo>().ToArray());
        Assert.Equal(flow, w.Map.DataStore.GetSpan<HydrologyInfo>().ToArray().Select(h => h.Flow).ToArray());
        Assert.False(w.Edits[t].IsEmpty);
    }
}
