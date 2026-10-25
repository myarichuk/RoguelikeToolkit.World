using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Endorheic hydrology: enclosed basins pond lakes with inlet rivers, arid
/// pits bake into playas, and desert rivers die in sinks. Synthetic terrains
/// (fixed topology + hand-set fields) pin the mechanics; real-world seeds pin
/// the integrated behavior.
/// </summary>
public class LakeTests
{
    private static int[] RingsFrom(WorldDataStore store, int start)
    {
        var ring = new int[store.TileCount];
        for (int i = 0; i < ring.Length; i++) ring[i] = -1;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        ring[start] = 0;
        System.Span<int> scratch = stackalloc int[6];
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int adjacent = store.GetAdjacent(cur, scratch);
            for (int k = 0; k < adjacent; k++)
            {
                if (ring[scratch[k]] < 0) { ring[scratch[k]] = ring[cur] + 1; queue.Enqueue(scratch[k]); }
            }
        }
        return ring;
    }

    private static WorldMap LakeBasinMap()
    {
        // Enclosed funnel with a carved inlet valley and a spillway channel:
        // the pit must pond, the valley must feed it a Lake-terminal river,
        // and the spillway must carry a Sea-terminal outlet river.
        var map = new WorldMap(4);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.DataStore.Allocate();

        var store = map.DataStore;
        int pit = 1000;
        var ring = RingsFrom(store, pit);
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        for (int i = 0; i < store.TileCount; i++)
        {
            elev[i] = new ElevationInfo { Height = ring[i] <= 8 ? 0.06f + 0.10f * ring[i] : -0.5f };
            climate[i] = new ClimateInfo { Temperature = 0.5f, Precipitation = 0.7f };
        }

        var fromZero = RingsFrom(store, 0);
        int valleyStart = -1, best = -1;
        for (int i = 0; i < store.TileCount; i++)
            if (ring[i] == 1 && fromZero[i] > best) { best = fromZero[i]; valleyStart = i; }
        CarveChannel(store, elev, ring, valleyStart, 8, r => 0.10f + 0.08f * r);

        var fromValley = RingsFrom(store, valleyStart);
        int spillStart = -1;
        best = -1;
        for (int i = 0; i < store.TileCount; i++)
            if (ring[i] == 1 && fromValley[i] > best) { best = fromValley[i]; spillStart = i; }
        CarveChannel(store, elev, ring, spillStart, 9, r => 0.30f - 0.01f * r);

        return map;
    }

    private static void CarveChannel(
        WorldDataStore store, System.Span<ElevationInfo> elev, int[] ring, int start, int maxRing, System.Func<int, float> floor)
    {
        System.Span<int> scratch = stackalloc int[6];
        int cur = start;
        elev[cur] = new ElevationInfo { Height = floor(ring[cur]) };
        while (ring[cur] < maxRing)
        {
            int adjacent = store.GetAdjacent(cur, scratch);
            int next = -1;
            for (int k = 0; k < adjacent; k++)
                if (ring[scratch[k]] == ring[cur] + 1) { next = scratch[k]; break; }
            if (next < 0) break;
            cur = next;
            elev[cur] = new ElevationInfo { Height = floor(ring[cur]) };
        }
    }

    [Fact]
    public void EnclosedBasin_PondsLakeWithInletAndOutletRivers()
    {
        using var map = LakeBasinMap();
        new HydrologyStage().Execute(map);
        var store = map.DataStore;
        var hydro = store.GetSpan<HydrologyInfo>();

        // The pit ponds above sea level.
        Assert.True(hydro[1000].LakeDepth > 0.05f, $"Pit depth {hydro[1000].LakeDepth}");
        Assert.True(hydro[1000].WaterBodyId >= 0);

        var rivers = new RiverCatalog();
        var bodies = new WaterBodyCatalog();
        HydrologyStage.PopulateCatalogs(map, rivers, bodies);

        // The lake is cataloged with a shoreline and ponded tiles.
        WaterBody? lake = null;
        foreach (var b in bodies.Bodies)
            if (b.Kind == WaterBodyKind.Lake && b.Tiles.Contains(1000)) lake = b;
        Assert.NotNull(lake);
        Assert.NotEmpty(lake!.Boundary);
        foreach (int t in lake.Tiles) Assert.True(hydro[t].LakeDepth > 0f);

        // An inlet river ends in the lake; an outlet river leaves for the sea.
        bool hasInlet = false, hasOutlet = false;
        foreach (var r in rivers.Rivers)
        {
            // Tributaries inherit the trunk's terminal at their confluence, so only the trunk
            // (the reach that actually ends in the lake) has to end on a ponded tile.
            if (r.Terminal == RiverTerminal.Lake && hydro[r.Path[^1]].LakeDepth > 0f)
                hasInlet = true;
            if (r.Terminal == RiverTerminal.Sea) hasOutlet = true;
        }
        Assert.True(hasInlet, "Expected a Lake-terminal inlet river");
        Assert.True(hasOutlet, "Expected a Sea-terminal outlet river");
    }

    [Fact]
    public void AridPit_BakesPlayaWithoutLake()
    {
        using var map = new WorldMap(2);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.DataStore.Allocate();

        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        for (int i = 0; i < store.TileCount; i++)
        {
            elev[i] = new ElevationInfo { Height = 0.3f };
            climate[i] = new ClimateInfo { Temperature = 0.8f, Precipitation = 0.1f };
        }
        int pit = 50;
        elev[pit] = new ElevationInfo { Height = 0.05f };
        System.Span<int> scratch = stackalloc int[6];
        int adjacent = store.GetAdjacent(pit, scratch);
        for (int k = 0; k < adjacent; k++)
            elev[scratch[k]] = new ElevationInfo { Height = 0.5f };
        elev[0] = new ElevationInfo { Height = -1f };

        new HydrologyStage().Execute(map);
        var hydro = store.GetSpan<HydrologyInfo>();

        Assert.Equal(0f, hydro[pit].LakeDepth);
        Assert.Equal(1, hydro[pit].IsPlaya);
        Assert.Equal(-1, hydro[pit].WaterBodyId);

        var ranges = new RangeCatalog();
        RangeCatalogBuilder.Populate(map, ranges);
        bool playaFound = false;
        foreach (var f in ranges.Features)
            if (f.Kind == RangeKind.Playa && f.Tiles.Contains(pit)) playaFound = true;
        Assert.True(playaFound, "Expected a Playa feature on the dry sump");
    }

    [Fact]
    public void DesertCrossing_RiversDieInSinks()
    {
        // Wet highland cap draining across a vast desert to a far ocean:
        // some rivers must evaporate mid-course. The ground still drains to the sea, so they dry out; they are not
        // endorheic sinks.
        using var map = new WorldMap(3);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.DataStore.Allocate();

        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        var vectors = store.GetTileVectors();
        for (int i = 0; i < store.TileCount; i++)
        {
            var g = vectors[i].ToGeoCoord();
            float h = g.Latitude > 45 ? 0.5f
                : g.Latitude < -70 ? -0.5f
                : 0.30f - (float)(45 - g.Latitude) * 0.0015f;
            elev[i] = new ElevationInfo { Height = h };
            bool wet = g.Latitude > 45;
            climate[i] = new ClimateInfo
            {
                Temperature = wet ? 0.35f : 0.9f,
                Precipitation = wet ? 1.0f : 0.05f
            };
        }

        new HydrologyStage().Execute(map);
        var hydro = store.GetSpan<HydrologyInfo>();
        var rivers = new RiverCatalog();
        var bodies = new WaterBodyCatalog();
        HydrologyStage.PopulateCatalogs(map, rivers, bodies);

        bool hasSink = false;
        foreach (var r in rivers.Rivers)
        {
            int tail = r.Path[^1];
            if (r.Terminal is RiverTerminal.Sink or RiverTerminal.DriesOut)
            {
                hasSink |= r.Terminal == RiverTerminal.DriesOut;
                Assert.True(elev[tail].Height >= ElevationGenerationStage.SeaLevel);
                Assert.True(hydro[tail].LakeDepth <= 0f);
                Assert.Equal(r.Terminal == RiverTerminal.Sink ? 1 : 0, hydro[tail].Endorheic);
            }
        }
        Assert.True(hasSink, "Expected a desert river that dries out on its way");
    }

    [Fact]
    public void RealWorld_LakeRiversExistAndAreDeterministic()
    {
        using var first = new WorldBuilder().WithSize(3).WithSeed(12).Build();
        using var second = new WorldBuilder().WithSize(3).WithSeed(12).Build();

        bool hasLakeRiver = false;
        foreach (var r in first.Rivers.Rivers)
            if (r.Terminal == RiverTerminal.Lake) hasLakeRiver = true;
        Assert.True(hasLakeRiver, "Expected Lake-terminal rivers for size 3, seed 12");

        Assert.Equal(first.Rivers.Rivers.Count, second.Rivers.Rivers.Count);
        for (int k = 0; k < first.Rivers.Rivers.Count; k++)
        {
            var a = first.Rivers.Rivers[k];
            var b = second.Rivers.Rivers[k];
            Assert.Equal(a.Terminal, b.Terminal);
            Assert.Equal(a.TerminalBody, b.TerminalBody);
            Assert.Equal(a.MouthFlow, b.MouthFlow);
            Assert.Equal(a.Path, b.Path);
        }
    }

    /// <summary>One-tile pit on a plateau, solved directly with hand-set water terms and a chosen land cover.</summary>
    private static (LakeSolution Solution, int Pit) SolvePit(BiomeType cover, float runoffAtPit = 1.0f, float evap = 0.9f)
    {
        var map = new WorldMap(2);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();
        var store = map.DataStore;
        int n = store.TileCount;
        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = 0.3f;
        int pit = 50;
        heights[pit] = 0.05f;
        System.Span<int> scratch = stackalloc int[6];
        int adjacent = store.GetAdjacent(pit, scratch);
        for (int k = 0; k < adjacent; k++) heights[scratch[k]] = 0.5f;
        heights[0] = -1f;

        var filled = new float[n];
        Hydrography.ComputeFilledSurface(store, heights, filled);
        var receivers = new int[n];
        var depth = new int[n];
        Hydrography.ComputeReceivers(store, filled, receivers, depth);
        var runoff = new float[n];
        runoff[pit] = runoffAtPit;
        var flow = new float[n];
        Hydrography.AccumulateFlow(store, filled, receivers, runoff, flow, default, default, depth);
        var evaps = new float[n];
        for (int i = 0; i < n; i++) evaps[i] = evap;
        var covers = new BiomeType[n];
        for (int i = 0; i < n; i++) covers[i] = cover;
        var solution = LakeSolver.Solve(store, heights, filled, flow, receivers, runoff, evaps, 0f, covers);
        map.Dispose();
        return (solution, pit);
    }

    [Fact]
    public void LandCover_DecidesWhetherTheSameBasinHoldsALake()
    {
        // Identical rainfall and identical temperature-driven evaporation: only the ground and the air differ.
        var (humid, pit) = SolvePit(BiomeType.Jungle);
        Assert.True(humid.Depth[pit] > 0.3f, "humid-air, peat-bedded basin should hold a deep lake");
        Assert.False(humid.IsPlaya[pit]);

        var (desert, _) = SolvePit(BiomeType.Desert);
        Assert.Equal(0f, desert.Depth[pit]);
        Assert.True(desert.IsPlaya[pit], "hot dry air and a leaking sand bed should leave a dry playa");
        Assert.True(desert.Endorheic[pit] && desert.Sink[pit] && desert.Saline[pit]);
    }

    [Fact]
    public void Seepage_LeaksPorousBedsAndSealsTheRest()
    {
        Assert.True(LandCover.Seepage(BiomeType.Desert) > LandCover.Seepage(BiomeType.Plains));
        Assert.True(LandCover.Seepage(BiomeType.Plains) > LandCover.Seepage(BiomeType.Jungle));
        foreach (var sealedBed in new[] { BiomeType.Swamp, BiomeType.Tundra, BiomeType.SaltFlat })
            Assert.Equal(0f, LandCover.Seepage(sealedBed));
    }

    [Fact]
    public void ClosedLake_IsEndorheicAndSaline_OpenFreshLakeIsNeither()
    {
        // Rainfall just above the open-lake threshold fills the basin and spills it.
        var (open, pit) = SolvePit(BiomeType.Plains, runoffAtPit: 3.0f);
        Assert.Single(open.Lakes);
        Assert.True(open.Lakes[0].IsOpen);
        Assert.False(open.Lakes[0].IsSaline);
        Assert.False(open.Endorheic[pit]);
        Assert.False(open.Saline[pit]);
        Assert.True(open.Lakes[0].Flushing > 0.5f);
    }

    [Fact]
    public void NearlyDryOpenLake_IsSalineEvenThoughItSpills()
    {
        // Barely more inflow than evaporation: it overflows, but evaporates >2/3 of what it receives.
        var (marginal, pit) = SolvePit(BiomeType.Plains, runoffAtPit: 0.95f, evap: 0.9f);
        Assert.Single(marginal.Lakes);
        var lake = marginal.Lakes[0];
        Assert.True(lake.IsOpen);
        Assert.True(lake.Flushing * Lake.SalineConcentration < 1f, $"flushing {lake.Flushing}");
        Assert.True(lake.IsSaline);
        Assert.True(marginal.Saline[pit]);
        Assert.False(marginal.Endorheic[pit], "an overflowing lake still reaches the sea");
    }

    [Fact]
    public void MinimumLakeDepth_DependsOnWhatGrowsInTheHollow()
    {
        Assert.True(LandCover.MinDepthScale(BiomeType.Swamp) > 1f);
        Assert.True(LandCover.MinDepthScale(BiomeType.Tundra) < 1f);
        Assert.Equal(1f, LandCover.MinDepthScale(BiomeType.Plains));
    }

    [Fact]
    public void RunoffCoefficients_AreMildAndOrdered()
    {
        Assert.Equal(1f, LandCover.RunoffCoefficient(BiomeType.Plains));
        Assert.True(LandCover.RunoffCoefficient(BiomeType.Forest) < 1f);
        Assert.True(LandCover.RunoffCoefficient(BiomeType.Desert) < LandCover.RunoffCoefficient(BiomeType.RockDesert));
        Assert.True(LandCover.RunoffCoefficient(BiomeType.Badlands) > 1f);
        foreach (BiomeType b in System.Enum.GetValues<BiomeType>())
        {
            Assert.InRange(LandCover.RunoffCoefficient(b), 0.5f, 1.5f);
            Assert.InRange(LandCover.EvaporationFactor(b), 0.5f, 1.5f);
        }
    }

    [Fact]
    public void AridPit_BasinAndItsCatchmentAreEndorheic()
    {
        using var map = new WorldMap(2);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore));
        map.RegisterLayer<HydrologyInfo>(new HydrologyLayer(map.DataStore));
        map.DataStore.Allocate();

        var store = map.DataStore;
        var elev = store.GetSpan<ElevationInfo>();
        var climate = store.GetSpan<ClimateInfo>();
        for (int i = 0; i < store.TileCount; i++)
        {
            elev[i] = new ElevationInfo { Height = 0.3f };
            climate[i] = new ClimateInfo { Temperature = 0.8f, Precipitation = 0.1f };
        }
        int pit = 50;
        elev[pit] = new ElevationInfo { Height = 0.05f };
        System.Span<int> scratch = stackalloc int[6];
        int adjacent = store.GetAdjacent(pit, scratch);
        for (int k = 0; k < adjacent; k++)
            elev[scratch[k]] = new ElevationInfo { Height = 0.5f };
        elev[0] = new ElevationInfo { Height = -1f };

        new HydrologyStage().Execute(map);
        var hydro = store.GetSpan<HydrologyInfo>();

        Assert.Equal(1, hydro[pit].Endorheic);
        Assert.Equal(1, hydro[pit].Saline);
        // Ground that drains to the open ocean is not endorheic.
        Assert.Equal(0, hydro[0].Endorheic);
        int exorheic = 0;
        for (int i = 0; i < store.TileCount; i++)
            if (elev[i].Height >= 0f && hydro[i].Endorheic == 0) exorheic++;
        Assert.True(exorheic > store.TileCount / 2, "most of the plateau drains to the sea");
    }

    [Fact]
    public void RealWorlds_EndorheicFlagIsConsistentWithRiverTerminals()
    {
        foreach (int seed in new[] { 42, 7, 99 })
        {
            using var world = new WorldBuilder().WithSize(5).WithSeed(seed).Build();
            var hydro = world.Map.DataStore.GetSpan<HydrologyInfo>();
            foreach (var river in world.Rivers.Rivers)
            {
                foreach (int t in river.Path)
                {
                    if (river.Terminal == RiverTerminal.Sea)
                        Assert.True(hydro[t].Endorheic == 0, $"seed {seed} river {river.Id} reaches the sea through endorheic tile {t}");
                }
            }
            for (int i = 0; i < hydro.Length; i++)
            {
                // Open water on a terminal lake is brine; a playa floor is both dry and saline.
                if (hydro[i].IsPlaya == 1)
                {
                    Assert.Equal(1, hydro[i].Saline);
                    Assert.Equal(1, hydro[i].Endorheic);
                }
            }
        }
    }

    [Fact]
    public void LandCover_ChangesOnlyTheWaterBalance_NotDeterminism()
    {
        using var a = new WorldBuilder().WithSize(4).WithSeed(11).Build();
        using var b = new WorldBuilder().WithSize(4).WithSeed(11).Build();
        var ha = a.Map.DataStore.GetSpan<HydrologyInfo>();
        var hb = b.Map.DataStore.GetSpan<HydrologyInfo>();
        for (int i = 0; i < ha.Length; i++)
        {
            Assert.Equal(ha[i].Endorheic, hb[i].Endorheic);
            Assert.Equal(ha[i].Saline, hb[i].Saline);
            Assert.Equal(ha[i].LakeDepth, hb[i].LakeDepth);
        }
    }
}
