using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Plate boundaries between oceanic plates used to paint a continuous 1-6 tile land ribbon along the
/// whole border. The detector finds them; the stage after erosion breaks them into strings of islands.
/// </summary>
public class IslandChainTests
{
    private const int Size = 6;
    private static readonly int[] Seeds = { 1, 2, 3, 5, 7, 11 };

    // A run this long is a ribbon, not an island arc: the repaired beads are at most ~MaxBead rings plus their width.
    private static readonly IslandChainOptions Audit = new() { MinLength = 20 };

    private static World Build(int seed, bool repair)
    {
        var b = new WorldBuilder().WithSize(Size).WithSeed(seed);
        if (!repair) b.WithoutIslandChains();
        return b.Build();
    }

    private static int LongestRun(World w)
    {
        var store = w.Map.DataStore;
        var chains = IslandChainDetector.Detect(store, store.GetSpan<ElevationInfo>(), store.GetSpan<TectonicPlate>(), Audit);
        return chains.Count == 0 ? 0 : chains.Max(c => c.Length);
    }

    private static int LandTiles(World w)
    {
        int n = 0;
        foreach (var e in w.Map.DataStore.GetSpan<ElevationInfo>()) if (e.Height > ElevationGenerationStage.SeaLevel) n++;
        return n;
    }

    [Fact]
    public void Unrepaired_WorldsHaveLongRibbons()
    {
        // Non-vacuity: if this stops holding the repair tests below prove nothing.
        foreach (int seed in Seeds)
        {
            using var w = Build(seed, repair: false);
            Assert.True(LongestRun(w) >= Audit.MinLength, $"seed {seed}: no ribbon to repair.");
        }
    }

    [Fact]
    public void Repair_RemovesLongRibbons_AndKeepsMostOfTheLand()
    {
        foreach (int seed in Seeds)
        {
            using var raw = Build(seed, repair: false);
            using var fixedUp = Build(seed, repair: true);
            int before = LongestRun(raw), after = LongestRun(fixedUp);
            Assert.True(after < before, $"seed {seed}: longest ribbon {before} -> {after}.");
            Assert.True(after < Audit.MinLength + 12, $"seed {seed}: ribbon of {after} rings survived.");
            // The beads stay land; only the straits between them go under.
            double kept = (double)LandTiles(fixedUp) / LandTiles(raw);
            Assert.True(kept > 0.90, $"seed {seed}: repair drowned {100 * (1 - kept):F1}% of the land.");
        }
    }

    [Fact]
    public void Repair_LeavesBeadsAsLand_NotJustDeletedChains()
    {
        using var raw = Build(3, repair: false);
        using var fixedUp = Build(3, repair: true);
        var store = raw.Map.DataStore;
        var chains = IslandChainDetector.Detect(store, store.GetSpan<ElevationInfo>(), store.GetSpan<TectonicPlate>());
        int chainTiles = chains.Sum(c => c.Tiles.Count);
        int stillLand = chains.SelectMany(c => c.Tiles).Count(t => fixedUp.Map.DataStore.GetSpan<ElevationInfo>()[t].Height > 0);
        Assert.True(chainTiles > 0);
        Assert.True(stillLand > chainTiles * 0.3, $"only {stillLand}/{chainTiles} chain tiles remain land: that is deletion, not islands.");
        Assert.True(stillLand < chainTiles, "no strait was cut.");
    }

    [Fact]
    public void Detector_OnlyFlagsOceanicCrustAtABoundary()
    {
        using var w = Build(11, repair: false);
        var store = w.Map.DataStore;
        var plates = store.GetSpan<TectonicPlate>();
        var elev = store.GetSpan<ElevationInfo>();
        var opts = new IslandChainOptions();
        var chains = IslandChainDetector.Detect(store, elev, plates, opts);
        Assert.NotEmpty(chains);
        foreach (var t in chains.SelectMany(c => c.Tiles))
        {
            Assert.True(elev[t].Height > ElevationGenerationStage.SeaLevel);
            Assert.Equal(CrustType.Oceanic, plates[t].Crust);
            Assert.NotEqual(PlateBoundaryType.None, plates[t].NearestBoundary);
            Assert.True(plates[t].BoundaryRings <= opts.BoundaryRings);
        }
        foreach (var c in chains)
        {
            Assert.True(c.Length >= opts.MinLength);
            Assert.Equal(0, c.Positions[c.Tiles.ToList().IndexOf(c.Anchor)]);
        }
    }

    [Fact]
    public void CoarseGrids_AreLeftAlone()
    {
        using var raw = new WorldBuilder().WithSize(3).WithSeed(7).WithoutIslandChains().Build();
        using var fixedUp = new WorldBuilder().WithSize(3).WithSeed(7).Build();
        var store = raw.Map.DataStore;
        Assert.True(store.TileCount < new IslandChainOptions().MinTileCount);
        Assert.Empty(IslandChainDetector.Detect(store, store.GetSpan<ElevationInfo>(), store.GetSpan<TectonicPlate>()));
        Assert.Equal(store.GetSpan<ElevationInfo>().ToArray(), fixedUp.Map.DataStore.GetSpan<ElevationInfo>().ToArray());
    }

    [Fact]
    public void Detector_IsPure()
    {
        using var w = Build(2, repair: false);
        var store = w.Map.DataStore;
        var before = store.GetSpan<ElevationInfo>().ToArray();
        var a = IslandChainDetector.Detect(store, store.GetSpan<ElevationInfo>(), store.GetSpan<TectonicPlate>());
        var b = IslandChainDetector.Detect(store, store.GetSpan<ElevationInfo>(), store.GetSpan<TectonicPlate>());
        Assert.Equal(before, store.GetSpan<ElevationInfo>().ToArray());
        Assert.Equal(a.Select(c => c.Anchor), b.Select(c => c.Anchor));
        Assert.Equal(a.Sum(c => c.Tiles.Count), b.Sum(c => c.Tiles.Count));
    }

    [Fact]
    public void Repair_IsDeterministic()
    {
        using var a = Build(7, repair: true);
        using var b = Build(7, repair: true);
        Assert.Equal(a.Map.DataStore.GetSpan<ElevationInfo>().ToArray(), b.Map.DataStore.GetSpan<ElevationInfo>().ToArray());
    }

    [Fact]
    public void Break_NeverDrownsHandEditedTiles()
    {
        using var w = Build(3, repair: false);
        var store = w.Map.DataStore;
        var plates = store.GetSpan<TectonicPlate>();
        var elev = store.GetSpan<ElevationInfo>().ToArray();

        var edits = new UserEditInfo[store.TileCount];
        // Sculpted tiles and pinned tiles are both off limits.
        for (int i = 0; i < edits.Length; i++)
            edits[i] = (i & 1) == 0 ? new UserEditInfo { HeightDelta = 0.2f } : new UserEditInfo { Flags = UserEditFlags.PinHeight, PinHeight = 0.3f };
        var protectedCopy = (ElevationInfo[])elev.Clone();
        Assert.Equal(0, IslandChainDetector.Break(store, protectedCopy, plates, 3, null, edits));
        Assert.Equal(elev, protectedCopy);

        var open = (ElevationInfo[])elev.Clone();
        Assert.True(IslandChainDetector.Break(store, open, plates, 3) > 0, "without edits the same call must cut straits.");
    }

    [Fact]
    public void Break_OnlyLowersTiles_AndOnlyBelowSeaLevel()
    {
        using var w = Build(5, repair: false);
        var store = w.Map.DataStore;
        var plates = store.GetSpan<TectonicPlate>();
        var raw = store.GetSpan<ElevationInfo>().ToArray();
        var cut = (ElevationInfo[])raw.Clone();
        int drowned = IslandChainDetector.Break(store, cut, plates, 5);
        Assert.True(drowned > 0);
        int changed = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i].Height == cut[i].Height) continue;
            changed++;
            Assert.True(raw[i].Height > 0 && cut[i].Height < 0, $"tile {i}: {raw[i].Height} -> {cut[i].Height}");
        }
        Assert.Equal(drowned, changed);
    }

    [Fact]
    public void Stage_TiesErosionsOrder_SoTheStableSortRunsItAfterErosion()
    {
        Assert.Equal(StageOrder.Erosion, new IslandChainStage().Order);
        Assert.Equal(new[] { typeof(ElevationInfo), typeof(TectonicPlate) }, new IslandChainStage().Reads);
        Assert.Equal(new[] { typeof(ElevationInfo) }, new IslandChainStage().Writes);
    }

    [Fact]
    public void WithIslandChains_ConfiguresTheStage()
    {
        // A MinLength nothing reaches turns the repair into a no-op: heights equal the unrepaired world's.
        using var raw = Build(3, repair: false);
        using var inert = new WorldBuilder().WithSize(Size).WithSeed(3).WithIslandChains(s => s.Options.MinLength = 100_000).Build();
        Assert.Equal(raw.Map.DataStore.GetSpan<ElevationInfo>().ToArray(), inert.Map.DataStore.GetSpan<ElevationInfo>().ToArray());
    }

    [Fact]
    public void Recompute_KeepsTheRepair()
    {
        using var w = new WorldBuilder().WithSize(Size).WithSeed(4).WithUserEdits().Build();
        var before = w.Map.DataStore.GetSpan<ElevationInfo>().ToArray();
        w.Recompute();
        Assert.Equal(before, w.Map.DataStore.GetSpan<ElevationInfo>().ToArray());
    }
}
