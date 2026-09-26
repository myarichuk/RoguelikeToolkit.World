using System.Collections.Generic;
using RoguelikeToolkit.World.Core;
using Xunit;

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
            if (r.Terminal == RiverTerminal.Lake)
            {
                hasInlet = true;
                Assert.True(hydro[r.Path[^1]].LakeDepth > 0f);
            }
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
        // some rivers must evaporate mid-course (sink terminal on dry land).
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
            if (r.Terminal == RiverTerminal.Sink)
            {
                hasSink = true;
                Assert.True(elev[tail].Height >= ElevationGenerationStage.SeaLevel);
                Assert.True(hydro[tail].LakeDepth <= 0f);
            }
        }
        Assert.True(hasSink, "Expected a Sink-terminal desert river");
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
}
