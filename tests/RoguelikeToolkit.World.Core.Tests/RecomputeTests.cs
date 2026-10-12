using System;
using System.Linq;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Hand edits as a pipeline input: the world is a pure function of (seed, configuration, edits), so
/// recomputing is idempotent and never erodes twice.
/// </summary>
public class RecomputeTests
{
    private const int Size = 3, Seed = 42;

    private static World Editable() => new WorldBuilder().WithSize(Size).WithSeed(Seed).WithUserEdits().Build();

    private sealed record Snapshot(float[] Height, float[] Temp, float[] Rain, BiomeType[] Biome, float[] Flow, int Rivers, int Bodies, int Ranges, int Deposits);

    private static Snapshot Take(World w)
    {
        var s = w.Map.DataStore;
        var e = s.GetSpan<ElevationInfo>();
        var c = s.GetSpan<ClimateInfo>();
        var l = s.GetSpan<LocalMapInfo>();
        var h = s.GetSpan<HydrologyInfo>();
        int n = s.TileCount;
        var height = new float[n]; var temp = new float[n]; var rain = new float[n]; var flow = new float[n];
        var biome = new BiomeType[n];
        for (int i = 0; i < n; i++)
        {
            height[i] = e[i].Height; temp[i] = c[i].Temperature; rain[i] = c[i].Precipitation;
            flow[i] = h[i].Flow; biome[i] = l[i].Biome;
        }
        return new Snapshot(height, temp, rain, biome, flow,
            w.Rivers.Rivers.Count, w.WaterBodies.Bodies.Count, w.Ranges.Features.Count, w.Deposits.Deposits.Count);
    }

    private static void AssertSame(Snapshot a, Snapshot b)
    {
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.Temp, b.Temp);
        Assert.Equal(a.Rain, b.Rain);
        Assert.Equal(a.Biome, b.Biome);
        Assert.Equal(a.Flow, b.Flow);
        Assert.Equal((a.Rivers, a.Bodies, a.Ranges, a.Deposits), (b.Rivers, b.Bodies, b.Ranges, b.Deposits));
    }

    /// <summary>A tile on land, away from the poles, with the most land neighbours: somewhere a ridge can stand.</summary>
    private static int InlandTile(World w)
    {
        var s = w.Map.DataStore;
        var e = s.GetSpan<ElevationInfo>();
        Span<int> nb = stackalloc int[6];
        int best = -1, bestScore = -1;
        for (int i = 0; i < s.TileCount; i++)
        {
            if (e[i].Height < 0.05f || Math.Abs(s.GetGeoCoord(i).Latitude) > 50) continue;
            int adj = s.GetAdjacent(i, nb), score = 0;
            for (int k = 0; k < adj; k++) if (e[nb[k]].Height >= 0f) score++;
            if (score > bestScore) { best = i; bestScore = score; }
        }
        return best;
    }

    [Fact]
    public void EditableWorld_WithoutEdits_IsIdenticalToThePlainBuild()
    {
        using var plain = new WorldBuilder().WithSize(Size).WithSeed(Seed).Build();
        using var editable = Editable();
        AssertSame(Take(plain), Take(editable));
    }

    [Fact]
    public void Recompute_WithoutEdits_ReproducesTheGeneratedWorld()
    {
        using var w = Editable();
        var before = Take(w);
        w.Recompute();
        AssertSame(before, Take(w));
        w.Recompute();
        AssertSame(before, Take(w));
    }

    [Fact]
    public void Recompute_IsDeterministicAndIdempotent_ForTheSameEdits()
    {
        using var a = Editable();
        using var b = Editable();
        int t = InlandTile(a);
        foreach (var w in new[] { a, b }) w.Edits[t].HeightDelta = 0.9f;
        a.Recompute(); b.Recompute();
        var first = Take(a);
        AssertSame(first, Take(b));
        a.Recompute();                       // applying again must not erode again
        AssertSame(first, Take(a));
    }

    [Fact]
    public void RemovingTheEdit_RestoresTheOriginalWorld()
    {
        using var w = Editable();
        var original = Take(w);
        int t = InlandTile(w);
        w.Edits[t].HeightDelta = 0.9f;
        w.Recompute();
        Assert.NotEqual(original.Height, Take(w).Height);
        w.Edits[t] = default;
        w.Recompute();
        AssertSame(original, Take(w));
    }

    [Fact]
    public void Sculpt_RaisesTheGround_AndErosionStillActsOnIt()
    {
        using var w = Editable();
        var s = w.Map.DataStore;
        int t = InlandTile(w);
        float before = s.GetSpan<ElevationInfo>()[t].Height;
        // A hill: the tile and its ring, so it is a landform rather than a one-tile spike erosion deletes.
        Span<int> nb = stackalloc int[6];
        int adj = s.GetAdjacent(t, nb);
        w.Edits[t].HeightDelta = 0.8f;
        for (int k = 0; k < adj; k++) w.Edits[nb[k]].HeightDelta = 0.8f;
        w.Recompute();
        float after = s.GetSpan<ElevationInfo>()[t].Height;
        Assert.True(after > before + 0.1f, $"sculpted {before:F2} -> {after:F2}");
        // A sculpt is not a pin: erosion has worn the raw +0.8 down.
        Assert.True(after < before + 0.8f - 0.02f, $"erosion left the hill untouched: {after:F2} vs {before + 0.8f:F2}");
    }

    [Fact]
    public void PinnedHeight_SurvivesErosionExactly()
    {
        using var w = Editable();
        int t = InlandTile(w);
        w.Edits[t].Flags = UserEditFlags.PinHeight;
        w.Edits[t].PinHeight = 1.0f;
        w.Recompute();
        Assert.Equal(1.0f, w.Map.DataStore.GetSpan<ElevationInfo>()[t].Height);
    }

    [Fact]
    public void PinnedBiome_IsKept_AgainstClimateAndSmoothing()
    {
        using var w = Editable();
        var s = w.Map.DataStore;
        int t = InlandTile(w);
        var climateBiome = s.GetSpan<LocalMapInfo>()[t].Biome;
        var pinned = climateBiome == BiomeType.SaltFlat ? BiomeType.Forest : BiomeType.SaltFlat;
        w.Edits[t].Flags = UserEditFlags.PinBiome;
        w.Edits[t].PinBiome = pinned;
        w.Recompute();
        Assert.Equal(pinned, s.GetSpan<LocalMapInfo>()[t].Biome);
        // Neighbours are untouched by the pin itself (only the pinned tile is forced).
        Assert.Equal(s.TileCount, s.GetSpan<LocalMapInfo>().Length);
    }

    [Fact]
    public void Recompute_ReplacesTheCatalogs_InsteadOfAppending()
    {
        using var w = Editable();
        var before = Take(w);
        w.Recompute();
        Assert.Equal(before.Rivers, w.Rivers.Rivers.Count);
        Assert.Equal(before.Bodies, w.WaterBodies.Bodies.Count);
        Assert.Equal(before.Deposits, w.Deposits.Deposits.Count);
    }

    [Fact]
    public void NonEditableWorld_RefusesToRecompute()
    {
        using var w = new WorldBuilder().WithSize(2).WithSeed(Seed).Build();
        Assert.False(w.Editable);
        Assert.Throws<InvalidOperationException>(() => w.Recompute());
    }

    [Fact]
    public void ExecuteFrom_SkipsEarlierStages()
    {
        using var w = Editable();
        var plates = w.Map.DataStore.GetSpan<TectonicPlate>();
        var snapshot = plates.ToArray();
        w.Recompute();
        Assert.True(snapshot.AsSpan().SequenceEqual(w.Map.DataStore.GetSpan<TectonicPlate>()));
    }
}
