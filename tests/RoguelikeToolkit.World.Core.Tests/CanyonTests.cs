using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Canyon (slot-gorge) physics: arid high-discharge reaches cutting relief.
/// Gates are unit-pinned; biome/feature agreement is pinned on a real world.
/// </summary>
public class CanyonTests
{
    [Fact]
    public void Gates_MatchTheColoradoRecipe()
    {
        // Powerful arid river + relief + slope: canyon.
        Assert.True(CanyonAnalysis.IsCanyonTile(12.0, 6.0, 0.30, 0.20, 0.05));
        // Weak flow: ordinary channel.
        Assert.False(CanyonAnalysis.IsCanyonTile(7.0, 6.0, 0.30, 0.20, 0.05));
        // Humid: gorges widen into valleys instead.
        Assert.False(CanyonAnalysis.IsCanyonTile(30.0, 6.0, 0.80, 0.20, 0.05));
        // No relief: nothing to cut through.
        Assert.False(CanyonAnalysis.IsCanyonTile(30.0, 6.0, 0.30, 0.02, 0.05));
        // Flat water: lakes and ponds never incise.
        Assert.False(CanyonAnalysis.IsCanyonTile(30.0, 6.0, 0.30, 0.20, 0.0));
    }

    [Fact]
    public void RealWorld_CanyonBiomeMatchesFeatures()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(12).Build();
        var store = world.Map.DataStore;
        var locals = store.GetSpan<LocalMapInfo>();

        var canyonTiles = new System.Collections.Generic.HashSet<int>();
        foreach (var f in world.Ranges.Features)
            if (f.Kind == RangeKind.Canyon)
                foreach (int t in f.Tiles) canyonTiles.Add(t);
        Assert.NotEmpty(canyonTiles);

        int biomeCanyons = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            if (locals[i].Biome == BiomeType.Canyon)
            {
                biomeCanyons++;
                Assert.Contains(i, canyonTiles);
            }
        }
        Assert.True(biomeCanyons > 0, "Expected Canyon biome tiles for size 3, seed 12");
        // Every canyon landform reads as Canyon ice-free, or Glacier when ice
        // fills the gorge (cover wins over landform, as with icy ranges).
        foreach (int t in canyonTiles)
            Assert.True(locals[t].Biome is BiomeType.Canyon or BiomeType.Glacier,
                $"Canyon tile {t} reads as {locals[t].Biome}");
    }

    [Fact]
    public void RealWorld_CanyonsAreAridRiverGorges()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(12).Build();
        var store = world.Map.DataStore;
        var hydro = store.GetSpan<HydrologyInfo>();
        var climate = store.GetSpan<ClimateInfo>();

        int checked_ = 0;
        foreach (var f in world.Ranges.Features)
        {
            if (f.Kind != RangeKind.Canyon) continue;
            foreach (int t in f.Tiles)
            {
                checked_++;
                Assert.Equal(1, hydro[t].IsRiver);
                Assert.True(climate[t].Precipitation <= 0.50f, $"Canyon tile {t} too wet");
                var info = world.GetTileFeatures(t);
                Assert.True(info.InCanyon);
            }
        }
        Assert.True(checked_ > 0);
    }

    [Fact]
    public void RealWorld_CanyonsAreDeterministic()
    {
        using var first = new WorldBuilder().WithSize(3).WithSeed(12).Build();
        using var second = new WorldBuilder().WithSize(3).WithSeed(12).Build();

        var a = new System.Collections.Generic.List<int>();
        var b = new System.Collections.Generic.List<int>();
        foreach (var f in first.Ranges.Features)
            if (f.Kind == RangeKind.Canyon) a.AddRange(f.Tiles);
        foreach (var f in second.Ranges.Features)
            if (f.Kind == RangeKind.Canyon) b.AddRange(f.Tiles);
        a.Sort();
        b.Sort();
        Assert.Equal(a, b);
    }
}
