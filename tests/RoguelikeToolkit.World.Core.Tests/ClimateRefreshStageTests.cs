using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>The second climate pass on eroded relief (opt-in via WithClimateRefresh).</summary>
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
            if (climate[i].Temperature != (float)Math.Clamp(t, 0.0, 1.0)) mismatches++;
        }
        return mismatches;
    }

    [Fact]
    public void Default_ClimateSeesTheRawReliefErosionStartedFrom()
    {
        using var w = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        Assert.True(CountTemperatureMismatches(w) > 0, "erosion lowers heights after the first climate pass; the default keeps that pass");
    }

    [Fact]
    public void Refresh_TemperatureFollowsTheFinalHeights()
    {
        using var w = new WorldBuilder().WithSize(4).WithSeed(42).WithClimateRefresh().Build();
        Assert.Equal(0, CountTemperatureMismatches(w));
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
        using var a = new WorldBuilder().WithSize(3).WithSeed(9).WithUserEdits().WithClimateRefresh().Build();
        using var b = new WorldBuilder().WithSize(3).WithSeed(9).WithUserEdits().WithClimateRefresh().Build();
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
        using var plain = new WorldBuilder().WithSize(4).WithSeed(7).Build();
        using var fresh = new WorldBuilder().WithSize(4).WithSeed(7).WithClimateRefresh().Build();
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
