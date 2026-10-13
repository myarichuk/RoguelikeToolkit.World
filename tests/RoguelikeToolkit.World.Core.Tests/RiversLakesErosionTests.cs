using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Regressions for three symptoms of one underlying model: ranges collapsed into low pancakes (a
/// uniform angle of repose against needle-sharp uplift), rivers could not start until the foothills
/// (one global discharge threshold), and lakes were either erased (talus fills every pit) or
/// confetti (hair-thin depressions counted as open water).
/// </summary>
public class RiversLakesErosionTests
{
    // ---- erosion -----------------------------------------------------------------------------

    /// <summary>Heights after tectonics, elevation and climate (pre-erosion), and after erosion with the given tweak.</summary>
    private static (float[] Pre, float[] Post) ErodedHeights(int size, int seed, Action<ErosionGenerationStage> tweak)
    {
        float[] Run(bool erode)
        {
            var b = new WorldBuilder().WithSize(size).WithSeed(seed).WithoutDefaultStages();
            b.AddStage(new TectonicPlateGenerationStage(12, seed));
            b.AddStage(new ElevationGenerationStage(seed));
            b.AddStage(new ClimateStage(seed));
            if (erode)
            {
                var stage = new ErosionGenerationStage();
                tweak(stage);
                b.AddStage(stage);
            }
            using var w = b.Build();
            return w.Map.DataStore.GetSpan<ElevationInfo>().ToArray().Select(e => e.Height).ToArray();
        }
        return (Run(false), Run(true));
    }

    private static double PeakMean(float[] heights, int count)
        => heights.OrderByDescending(h => h).Take(count).Average();

    [Fact]
    public void Erosion_LeavesRangesStanding_NotPancakes()
    {
        double kept = 0;
        foreach (var seed in new[] { 42, 1234 })
        {
            var (pre, post) = ErodedHeights(5, seed, _ => { });
            kept += PeakMean(post, 100) / PeakMean(pre, 100);
        }
        // The old uniform-talus model left the 100 highest tiles at roughly 40-45% of their uplifted height.
        Assert.True(kept / 2 > 0.55, $"Peaks kept only {kept / 2:P0} of their height");
    }

    [Fact]
    public void RockHardness_IsWhatKeepsRangesStanding()
    {
        var (pre, hard) = ErodedHeights(5, 42, _ => { });
        var (_, soft) = ErodedHeights(5, 42, e => { e.HardRockResistance = 0; e.HardRockTalus = 0; });
        Assert.True(PeakMean(hard, 100) > PeakMean(soft, 100) + 0.05,
            $"Hardness should hold peaks up: {PeakMean(hard, 100):F3} vs soft {PeakMean(soft, 100):F3} (pre {PeakMean(pre, 100):F3})");
    }

    [Fact]
    public void FluvialSediment_IsConserved_OverRealRelief()
    {
        // Climate present, so the transport-limited pass runs with real runoff and canyons.
        var (pre, post) = ErodedHeights(4, 7, e => { e.Iterations = 10; });
        Assert.Equal(pre.Sum(h => (double)h), post.Sum(h => (double)h), 2);
    }

    // ---- channels ----------------------------------------------------------------------------

    [Fact]
    public void ChannelThreshold_FallsWithSlope_AndIsBounded()
    {
        Assert.Equal(60f, Hydrography.ChannelThreshold(60f, 0f, 3f, 0.05f));
        Assert.Equal(60f, Hydrography.ChannelThreshold(60f, 0.5f, 0f, 0.05f));        // boost off
        Assert.Equal(15f, Hydrography.ChannelThreshold(60f, 0.05f, 3f, 0.05f), 3);     // full boost: 60 / (1 + 3)
        Assert.Equal(15f, Hydrography.ChannelThreshold(60f, 9f, 3f, 0.05f), 3);        // clamped, never lower
        Assert.True(Hydrography.ChannelThreshold(60f, 0.01f, 3f, 0.05f) > Hydrography.ChannelThreshold(60f, 0.03f, 3f, 0.05f));
    }

    [Fact]
    public void DrainageOrder_VisitsDonorsBeforeReceivers()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var store = world.Map.DataStore;
        var hydro = store.GetSpan<HydrologyInfo>();
        int n = store.TileCount;
        var surface = new float[n];
        for (int i = 0; i < n; i++) surface[i] = hydro[i].Surface;
        var receiver = new int[n];
        var depth = new int[n];
        Hydrography.ComputeReceivers(store, surface, receiver, depth);
        var order = Hydrography.DrainageOrder(surface, depth);
        var position = new int[n];
        for (int k = 0; k < n; k++) position[order[k]] = k;
        for (int i = 0; i < n; i++)
            if (receiver[i] >= 0)
                Assert.True(position[i] < position[receiver[i]], $"Tile {i} is visited after its receiver {receiver[i]}");
    }

    [Fact]
    public void SteepGround_StartsRivers_OnSmallerCatchments()
    {
        using var steep = new WorldBuilder().WithSize(5).WithSeed(42).Build();
        using var flat = new WorldBuilder().WithSize(5).WithSeed(42).WithHydrology(h => h.SteepBoost = 0).Build();
        int Count(World w) => w.Map.DataStore.GetSpan<HydrologyInfo>().ToArray().Count(h => h.IsRiver == 1);
        Assert.True(Count(steep) > Count(flat) * 1.2, $"steep-aware {Count(steep)} vs global threshold {Count(flat)}");

        // Without the boost the old invariant holds: every river tile clears the single threshold.
        float threshold = Hydrography.RiverThreshold(flat.TileCount, new HydrologyStage().RiverThresholdScale);
        foreach (var h in flat.Map.DataStore.GetSpan<HydrologyInfo>().ToArray().Where(h => h.IsRiver == 1))
            Assert.True(h.Flow >= threshold);
    }

    [Fact]
    public void Rivers_DoNotDieWhereSlopeRelaxes()
    {
        using var world = new WorldBuilder().WithSize(5).WithSeed(1234).Build();
        var store = world.Map.DataStore;
        var hydro = store.GetSpan<HydrologyInfo>();
        var elev = store.GetSpan<ElevationInfo>();
        int n = store.TileCount;
        var surface = new float[n];
        for (int i = 0; i < n; i++) surface[i] = hydro[i].Surface;
        var receiver = new int[n];
        Hydrography.ComputeReceivers(store, surface, receiver);
        var stage = new HydrologyStage();
        float floor = Hydrography.ChannelThreshold(Hydrography.RiverThreshold(n, stage.RiverThresholdScale), float.MaxValue, stage.SteepBoost, stage.SteepSlope);
        int checkedTiles = 0;
        for (int i = 0; i < n; i++)
        {
            if (hydro[i].IsRiver != 1) continue;
            int r = receiver[i];
            if (r < 0 || elev[r].Height < ElevationGenerationStage.SeaLevel || hydro[r].LakeDepth > 0f) continue;
            checkedTiles++;
            Assert.True(hydro[r].IsRiver == 1 || hydro[r].Flow < floor,
                $"River tile {i} drains into land tile {r} (flow {hydro[r].Flow}) that is not a river");
        }
        Assert.True(checkedTiles > 50, $"Only {checkedTiles} river links checked");
    }

    // ---- lakes -------------------------------------------------------------------------------

    private static WorldMap BasinWithDepth(float depth)
    {
        var map = new WorldMap(4);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.DataStore.Allocate();
        var store = map.DataStore;
        int pit = 1000;
        var ring = new int[store.TileCount];
        Array.Fill(ring, -1);
        var q = new Queue<int>();
        q.Enqueue(pit);
        ring[pit] = 0;
        Span<int> nb = stackalloc int[6];
        while (q.Count > 0)
        {
            int t = q.Dequeue();
            int a = store.GetAdjacent(t, nb);
            for (int k = 0; k < a; k++)
                if (ring[nb[k]] < 0) { ring[nb[k]] = ring[t] + 1; q.Enqueue(nb[k]); }
        }
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        for (int i = 0; i < store.TileCount; i++)
        {
            // A one-tile dimple of the given depth on ground that otherwise drains outward to the sea.
            float h = ring[i] == 0 ? 0.5f : ring[i] <= 8 ? 0.5f + depth - 0.05f * (ring[i] - 1) : -0.5f;
            elev[i] = new ElevationInfo { Height = h };
            climate[i] = new ClimateInfo { Temperature = 0.5f, Precipitation = 0.7f };
        }
        return map;
    }

    [Fact]
    public void HairThinHollow_IsMarsh_ButDeepHollow_IsLake()
    {
        using var shallow = BasinWithDepth(LakeSolver.MinLakeDepth * 0.4f);
        new HydrologyStage().Execute(shallow);
        Assert.Equal(0f, shallow.DataStore.GetSpan<HydrologyInfo>()[1000].LakeDepth);

        using var deep = BasinWithDepth(LakeSolver.MinLakeDepth * 3f);
        new HydrologyStage().Execute(deep);
        Assert.True(deep.DataStore.GetSpan<HydrologyInfo>()[1000].LakeDepth >= LakeSolver.MinLakeDepth);
    }

    [Fact]
    public void RealWorlds_HaveMeaningfulLakes_NotConfetti()
    {
        foreach (var seed in new[] { 42, 1234 })
        {
            using var world = new WorldBuilder().WithSize(5).WithSeed(seed).Build();
            var lakes = world.WaterBodies.Bodies.Where(b => b.Kind == WaterBodyKind.Lake).ToList();
            Assert.True(lakes.Count >= 8, $"seed {seed}: only {lakes.Count} lakes");
            // Confetti would be dominated by single tiles; require most lakes to span more than one.
            Assert.True(lakes.Count(b => b.Tiles.Count > 1) * 2 >= lakes.Count, $"seed {seed}: mostly single-tile lakes");
        }
    }

    [Fact]
    public void RealWorlds_HaveManyRivers_ReachingIntoUplands()
    {
        foreach (var seed in new[] { 42, 1234 })
        {
            using var world = new WorldBuilder().WithSize(5).WithSeed(seed).Build();
            Assert.True(world.Rivers.Rivers.Count >= 40, $"seed {seed}: only {world.Rivers.Rivers.Count} rivers");
            var elev = world.Map.DataStore.GetSpan<ElevationInfo>().ToArray();
            int upland = world.Rivers.Rivers.Count(r => elev[r.Path[0]].Height >= 0.3f);
            Assert.True(upland >= 3, $"seed {seed}: only {upland} rivers rise in the uplands");
        }
    }
}
