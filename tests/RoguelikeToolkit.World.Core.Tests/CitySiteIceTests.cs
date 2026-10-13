using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Settlement;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Sea ice as a settlement input: pack ice is not water access, winter ice costs a closed harbour.</summary>
public class CitySiteIceTests
{
    [Fact]
    public void WinterIce_CostsExactlyItsWeight_AndSaysSo()
    {
        var plain = new List<string>();
        var iced = new List<string>();
        double a = SettlementScoring.Score(true, BiomeType.Plains, 0.1f, 0, 0f, plain);
        double b = SettlementScoring.Score(true, BiomeType.Plains, 0.1f, 0, 0f, iced, winterIce: true);
        Assert.Equal(SettlementScoring.WinterIce, b - a, 12);
        Assert.DoesNotContain("winter-ice", plain);
        Assert.Contains("winter-ice", iced);
    }

    // Radius 1 km: only the tile itself and its neighbours can supply water, so the coast is what is being tested.
    private static CitySiteFilter CoastOnly() => new()
    {
        FreshWaterRadiusKm = 1, MaxElevation = 10f, MaxDanger = 255, TopN = int.MaxValue,
    };

    [Theory]
    [InlineData(7, 5)]
    [InlineData(1, 5)]
    [InlineData(3, 6)]
    public void PackIce_IsNotWaterAccess_AndSeasonalIceOnlyCostsTheHarbour(int seed, int size)
    {
        using var w = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
        var store = w.Map.DataStore;
        var ice = SeaIce.Mask(store);
        var rivers = new HashSet<int>(w.Rivers.Rivers.SelectMany(r => r.Path));
        var adj = new int[6];

        // Spans cannot be captured, so the water test is precomputed (the same three sources the scorer uses).
        var wet = new bool[store.TileCount];
        {
            var elev = store.GetSpan<ElevationInfo>();
            var hydro = store.GetSpan<HydrologyInfo>();
            for (int t = 0; t < wet.Length; t++) wet[t] = rivers.Contains(t) || hydro[t].WaterBodyId >= 0 || elev[t].Height < 0f;
        }
        bool Wet(int t) => wet[t];
        // Per land tile: does it touch water of each sort?
        var packOnly = new HashSet<int>();   // touches pack ice, and no other water
        var winterOnly = new HashSet<int>(); // touches seasonal ice, and no usable water besides (pack ice does not count)
        for (int i = 0; i < store.TileCount; i++)
        {
            if (Wet(i)) continue;
            int n = store.GetAdjacent(i, adj);
            bool pack = false, seasonal = false, other = false;
            for (int k = 0; k < n; k++)
            {
                int t = adj[k];
                if (!Wet(t)) continue;
                if (ice[t] == SeaIceKind.Perennial) pack = true;
                else if (ice[t] == SeaIceKind.Seasonal) seasonal = true;
                else other = true;
            }
            if (pack && !seasonal && !other) packOnly.Add(i);
            if (seasonal && !other) winterOnly.Add(i); // pack beside it is not usable water, so it changes nothing
        }
        // Positive controls: without them the assertions below would pass on a world that never exercises the rule
        // (size 4 has almost no land on the pack, so the worlds here are size 5 and 6).
        Assert.NotEmpty(packOnly);
        Assert.NotEmpty(winterOnly);

        var sites = w.ScoreCitySites(CoastOnly());
        var byTile = sites.ToDictionary(s => s.TileIndex);

        // A coast that is only pack ice has no water: the filter drops it.
        Assert.DoesNotContain(packOnly, byTile.ContainsKey);
        // A coast that is only winter ice keeps its water and carries the marker.
        var marked = winterOnly.Where(byTile.ContainsKey).ToList();
        Assert.NotEmpty(marked);
        foreach (int t in marked)
        {
            var reasons = byTile[t].Reasons.Split(',');
            Assert.Contains("water", reasons);
            Assert.Contains("winter-ice", reasons);
        }
        // And nowhere else: the marker means "the only usable water is seasonal ice".
        foreach (var s in sites.Where(s => s.Reasons.Split(',').Contains("winter-ice")))
            Assert.Contains(s.TileIndex, winterOnly);
    }

    [Fact]
    public void WithoutAClimateLayer_NothingFreezes_AndTheScorerStillRuns()
    {
        using var w = new WorldBuilder().WithSize(3).WithSeed(7).Build();
        var sites = w.ScoreCitySites();
        Assert.NotEmpty(sites);
        Assert.DoesNotContain(sites, s => s.Reasons.Split(',').Contains("winter-ice") && s.Biome == BiomeType.Ocean);
    }
}
