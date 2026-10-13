using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Tectonics;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>The second climate pass on eroded relief (the default; WithClimateRefresh(false) is the single-pass pipeline).</summary>
public class ClimateRefreshStageTests
{
    private static int CountTemperatureMismatches(World w)
    {
        var store = w.Map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        var vectors = store.GetTileVectors();
        int mismatches = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            double t = ClimateModel.Temperature(vectors[i], Math.Abs(vectors[i].ToGeoCoord().Latitude), elev[i].Height, ClimateModel.DefaultLapseRate, w.Planet);
            // The ocean currents' share is exposed as OceanHeat (a float, so match to its rounding), the rest is the model.
            if (Math.Abs(climate[i].Temperature - Math.Clamp(t + climate[i].OceanHeat, 0.0, 1.0)) > 1e-5) mismatches++;
        }
        return mismatches;
    }

    [Fact]
    public void Disabled_ClimateSeesTheRawReliefErosionStartedFrom()
    {
        using var w = new WorldBuilder().WithSize(4).WithSeed(42).WithClimateRefresh(false).Build();
        Assert.True(CountTemperatureMismatches(w) > 0, "erosion lowers heights after the first climate pass; without the refresh that pass is kept");
    }

    [Fact]
    public void Default_TemperatureFollowsTheFinalHeights()
    {
        using var w = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        Assert.Equal(0, CountTemperatureMismatches(w));
    }

    [Fact]
    public void Default_LandBornOfErosion_IsNotLeftWithTheOceansRainfall()
    {
        // Erosion fills shallows, so the coastline moves after the first climate pass. Without the refresh every
        // tile that turned from sea into land kept the ocean's 0.55 precipitation floor.
        const int seed = 42;
        using var staged = new WorldBuilder().WithSize(4).WithSeed(seed).WithoutDefaultStages()
            .AddStage(new TectonicPlateGenerationStage(12, seed)).AddStage(new ElevationGenerationStage(seed)).AddStage(new ClimateStage(seed)).Build();
        var rawElev = staged.Map.DataStore.GetSpan<ElevationInfo>().ToArray();
        var rawRain = staged.Map.DataStore.GetSpan<ClimateInfo>().ToArray();

        using var refreshed = new WorldBuilder().WithSize(4).WithSeed(seed).Build();
        using var legacy = new WorldBuilder().WithSize(4).WithSeed(seed).WithClimateRefresh(false).Build();
        var finalElev = refreshed.Map.DataStore.GetSpan<ElevationInfo>();
        var withRefresh = refreshed.Map.DataStore.GetSpan<ClimateInfo>();
        var without = legacy.Map.DataStore.GetSpan<ClimateInfo>();

        double sumWith = 0, sumWithout = 0; int born = 0;
        for (int i = 0; i < finalElev.Length; i++)
        {
            if (rawElev[i].Height >= ElevationGenerationStage.SeaLevel || finalElev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            born++;
            Assert.True(rawRain[i].Precipitation >= 0.55f); // the premise: the first pass saw ocean here (floor 0.55)
            sumWith += withRefresh[i].Precipitation;
            sumWithout += without[i].Precipitation;
        }
        Assert.True(born > 50, $"only {born} tiles turned into land; the premise needs a real coastline shift");
        Assert.True(sumWith / born < sumWithout / born - 0.02, $"refreshed {sumWith / born:F3} vs stale {sumWithout / born:F3}");
    }

    [Fact]
    public void Refresh_RunsBetweenErosionAndHydrology()
    {
        // Not an intermediate check: if it ran before erosion the temperature test above would fail,
        // if after hydrology the rivers would have been routed by the first pass. Pin the order itself.
        var stage = new ClimateRefreshStage(new ClimateStage(1));
        Assert.Equal(17, StageMetadata.GetOrder(stage));
        Assert.Equal(17, StageMetadata.GetOrder(new ErosionGenerationStage()));
        Assert.Equal(18, StageMetadata.GetOrder(new HydrologyStage()));
        Assert.Throws<ArgumentNullException>(() => new ClimateRefreshStage(null!));
    }

    [Fact]
    public void Refresh_IsDeterministic_AndSurvivesRecompute()
    {
        using var a = new WorldBuilder().WithSize(3).WithSeed(9).WithUserEdits().Build();
        using var b = new WorldBuilder().WithSize(3).WithSeed(9).WithUserEdits().Build();
        var ca = a.Map.DataStore.GetSpan<ClimateInfo>();
        var cb = b.Map.DataStore.GetSpan<ClimateInfo>();
        var before = ca.ToArray();
        for (int i = 0; i < ca.Length; i++) Assert.Equal(ca[i], cb[i]);

        a.Recompute(); // no edits: must land on the generated world, refresh pass included
        ca = a.Map.DataStore.GetSpan<ClimateInfo>();
        for (int i = 0; i < ca.Length; i++) Assert.Equal(before[i], ca[i]);
        Assert.Equal(0, CountTemperatureMismatches(a));
    }

    [Fact]
    public void Refresh_ChangesTheWorldButNotItsSkeleton()
    {
        // The legacy pipeline: the single pass, and no ocean currents (the default solves them only after erosion, so the
        // first pass that erosion reads must be the same in both).
        using var plain = new WorldBuilder().WithSize(4).WithSeed(7).WithClimateRefresh(false)
            .WithClimate(c => { c.OceanHeatStrength = 0.0; c.OceanRainGain = 0.0; }).Build();
        using var fresh = new WorldBuilder().WithSize(4).WithSeed(7).Build();
        var pe = plain.Map.DataStore.GetSpan<ElevationInfo>();
        var fe = fresh.Map.DataStore.GetSpan<ElevationInfo>();
        // Erosion reads the first-pass climate in both, so the relief is identical; only what is derived from the second pass moves.
        for (int i = 0; i < pe.Length; i++) Assert.Equal(pe[i].Height, fe[i].Height);
        var pl = plain.Map.DataStore.GetSpan<LocalMapInfo>();
        var fl = fresh.Map.DataStore.GetSpan<LocalMapInfo>();
        int diff = 0;
        for (int i = 0; i < pl.Length; i++) if (pl[i].Biome != fl[i].Biome) diff++;
        Assert.True(diff > 0);
    }
}
