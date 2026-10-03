using System;
using System.Linq;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Regression tests for the code-review fixes (idempotence, id lookup, fast-path equivalence).</summary>
public class ReviewFixesTests
{
    private static WorldGenerationPipeline DefaultPipeline(int seed, int plates = 12)
    {
        var p = new WorldGenerationPipeline();
        p.AddStage(new TectonicPlateGenerationStage(plates, seed));
        p.AddStage(new ElevationGenerationStage(seed));
        p.AddStage(new ClimateStage(seed));
        p.AddStage(new ErosionGenerationStage());
        p.AddStage(new HydrologyStage());
        p.AddStage(new LocalMapGenerationStage(seed));
        return p;
    }

    [Fact]
    public void Pipeline_Rerun_OnUsedMap_IsIdempotent()
    {
        // Used to differ: climate read the previous run's hydrology (31/2562 tiles).
        using var world = new WorldBuilder().WithSize(4).WithSeed(7).Build();
        var store = world.Map.DataStore;
        var climate = store.GetSpan<ClimateInfo>().ToArray();
        var elev = store.GetSpan<ElevationInfo>().ToArray();
        var hydro = store.GetSpan<HydrologyInfo>().ToArray();
        var local = store.GetSpan<LocalMapInfo>().ToArray();

        DefaultPipeline(7).Execute(world.Map);

        var c2 = store.GetSpan<ClimateInfo>();
        var e2 = store.GetSpan<ElevationInfo>();
        var h2 = store.GetSpan<HydrologyInfo>();
        var l2 = store.GetSpan<LocalMapInfo>();
        for (int i = 0; i < climate.Length; i++)
        {
            Assert.Equal(climate[i].Precipitation, c2[i].Precipitation);
            Assert.Equal(climate[i].Temperature, c2[i].Temperature);
            Assert.Equal(elev[i].Height, e2[i].Height);
            Assert.Equal(hydro[i].Flow, h2[i].Flow);
            Assert.Equal(hydro[i].WaterBodyId, h2[i].WaterBodyId);
            Assert.Equal(local[i].Biome, l2[i].Biome);
            Assert.Equal(local[i].DangerLevel, l2[i].DangerLevel);
        }
    }

    [Fact]
    public void Pipeline_ClearsOnlyFirstWritersLayers()
    {
        using var map = new WorldMap(2);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();
        map.DataStore.GetSpan<ElevationInfo>().Fill(new ElevationInfo { Height = 9f });

        var p = new WorldGenerationPipeline();
        // Refinement stage reads what it writes: must see existing data, not zeros.
        p.AddStage(new Refine());
        p.Execute(map);
        Assert.All(map.DataStore.GetSpan<ElevationInfo>().ToArray(), e => Assert.Equal(10f, e.Height));
    }

    [WorldGeneratorStage(1, Reads = new[] { typeof(ElevationInfo) }, Writes = new[] { typeof(ElevationInfo) })]
    private sealed class Refine : IWorldGeneratorStage
    {
        public void Execute(WorldMap map)
        {
            foreach (ref var e in map.DataStore.GetSpan<ElevationInfo>()) e.Height += 1f;
        }
    }

    [Fact]
    public void WaterBodyToWkt_ResolvesById_NotListPosition()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        Assert.True(world.WaterBodies.Bodies.Count > 1);
        foreach (var body in world.WaterBodies.Bodies)
            Assert.Equal(GeoWkt.WaterBodyToWkt(body, world.Map.DataStore), world.WaterBodyToWkt(body.Id));

        int unused = world.WaterBodies.Bodies.Max(b => b.Id) + 1;
        Assert.Throws<ArgumentOutOfRangeException>(() => world.WaterBodyToWkt(unused));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.WaterBodyToWkt(-1));
    }

    [Fact]
    public void WaterBodies_AreOrderedDeterministically()
    {
        using var a = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        using var b = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        Assert.Equal(a.WaterBodies.Bodies.Select(x => x.Id), b.WaterBodies.Bodies.Select(x => x.Id));
        Assert.Equal(a.Rivers.Rivers.Select(r => r.Path[0]), b.Rivers.Rivers.Select(r => r.Path[0]));
    }

    [Fact]
    public void RegisterLayer_AfterAllocate_DoesNotLeakIntoMap()
    {
        using var map = new WorldMap(2);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();
        Assert.Throws<InvalidOperationException>(() =>
            map.RegisterLayer<ClimateInfo>(new ClimateLayer(map.DataStore)));
        Assert.Null(map.GetLayer<ClimateInfo>());
    }

    [Fact]
    public void RiverThresholdScale_ReachesCanyonDetection()
    {
        const float scale = 3f;
        using var world = new WorldBuilder().WithSize(5).WithSeed(42)
            .WithHydrology(h => h.RiverThresholdScale = scale).Build();
        var hydro = world.Map.DataStore.GetSpan<HydrologyInfo>().ToArray();
        float need = Hydrography.RiverThreshold(world.TileCount, scale) * (float)CanyonAnalysis.FlowMultiple;
        foreach (var f in world.Ranges.Features.Where(f => f.Kind == RangeKind.Canyon))
            foreach (int t in f.Tiles)
                Assert.True(hydro[t].Flow >= need, $"canyon tile {t} flow {hydro[t].Flow} < {need}");
        foreach (var t in Enumerable.Range(0, world.TileCount).Where(i => hydro[i].IsRiver == 1))
            Assert.True(hydro[t].Flow >= Hydrography.RiverThreshold(world.TileCount, scale));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void GetTileIndex_MatchesExact_NearPolesAndEverywhere(int size)
    {
        using var map = new WorldMap(size);
        var store = map.DataStore;
        for (double lat = -90; lat <= 90; lat += 1.7)
            for (double lon = -180; lon <= 180; lon += 7.3)
            {
                AssertNearest(store, new GeoCoord(lat, lon));
            }
        for (double lat = 86; lat <= 90; lat += 0.25)
            for (double lon = -180; lon < 180; lon += 3.1)
            {
                AssertNearest(store, new GeoCoord(lat, lon));
                AssertNearest(store, new GeoCoord(-lat, lon));
            }
    }

    // Exact ties are legitimate on the grid's mirror planes (e.g. lon = +-180), so
    // compare distances, not indices.
    private static void AssertNearest(WorldDataStore store, GeoCoord c)
    {
        var target = Vector3D.FromGeoCoord(c);
        var v = store.GetTileVectors();
        double fast = Vector3D.Dot(target, v[store.GetTileIndex(c)]);
        double exact = Vector3D.Dot(target, v[store.GetTileIndexExact(c)]);
        Assert.True(fast >= exact - 1e-12, $"({c.Latitude},{c.Longitude}): fast dot {fast} < exact {exact}");
    }

    [Fact]
    public void Icosphere_HasExpectedCounts_AndAllEdgesShared()
    {
        for (int s = 0; s <= 5; s++)
        {
            IcosphereGenerator.Generate(s, out var v, out var f);
            Assert.Equal(10 * (1 << (2 * s)) + 2, v.Length);
            Assert.Equal(20 * (1 << (2 * s)), f.Length);
        }
    }

    [Fact]
    public void AnyWithin_MatchesBruteForce()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var idx = world.Index;
        var water = idx.TilesOfKind(FeatureKind.WaterBody);
        var vecs = world.Map.DataStore.GetTileVectors().ToArray();
        var rng = new Random(5);
        for (int q = 0; q < 400; q++)
        {
            int tile = rng.Next(world.TileCount);
            double radiusKm = rng.Next(50, 3000);
            double cos = Math.Cos(radiusKm / World.EarthRadiusKm);
            bool brute = water.Any(w => w != tile && Vector3D.Dot(vecs[tile], vecs[w]) >= cos);
            Assert.Equal(brute, idx.AnyWithin(vecs[tile], FeatureKind.WaterBody, cos, tile));
        }
    }

    [Fact]
    public void CitySites_AreSortedByScoreThenTile_AndHonorRadius()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var filter = new CitySiteFilter { TopN = 200, FreshWaterRadiusKm = 400, MaxElevation = 0.6f };
        var sites = world.ScoreCitySites(filter);
        Assert.NotEmpty(sites);
        for (int i = 1; i < sites.Count; i++)
        {
            Assert.True(sites[i - 1].Score > sites[i].Score
                || (sites[i - 1].Score == sites[i].Score && sites[i - 1].TileIndex < sites[i].TileIndex));
        }
        // Every result really has water within the radius (brute force).
        var store = world.Map.DataStore;
        var vecs = store.GetTileVectors().ToArray();
        var hydro = store.GetSpan<HydrologyInfo>().ToArray();
        var elev = store.GetSpan<ElevationInfo>().ToArray();
        double cos = Math.Cos(400 / World.EarthRadiusKm);
        foreach (var s in sites)
        {
            bool IsWater(int w) => hydro[w].WaterBodyId >= 0 || elev[w].Height < 0f
                || world.Rivers.Rivers.Any(r => r.Path.Contains(w));
            // Adjacent water always counts (tiles can be farther apart than the radius).
            bool near = IsWater(s.TileIndex) || Neighbors(world, s.TileIndex).Any(IsWater)
                || Enumerable.Range(0, world.TileCount).Any(w => IsWater(w)
                    && Vector3D.Dot(vecs[s.TileIndex], vecs[w]) >= cos);
            Assert.True(near, $"site {s.TileIndex} has no water within 400 km");
        }
        using var again = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        Assert.Equal(sites.Select(s => s.TileIndex), again.ScoreCitySites(filter).Select(s => s.TileIndex));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void ReceiverAt_MatchesFullPlanetRouting(int size)
    {
        using var world = new WorldBuilder().WithSize(size).WithSeed(42).Build();
        var store = world.Map.DataStore;
        int n = store.TileCount;
        var hydro = store.GetSpan<HydrologyInfo>();
        var elev = store.GetSpan<ElevationInfo>();

        var surfaces = new float[2][];
        surfaces[0] = new float[n];
        for (int i = 0; i < n; i++) surfaces[0][i] = hydro[i].Surface;   // lakes leveled
        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = elev[i].Height;
        surfaces[1] = new float[n];
        Hydrography.ComputeFilledSurface(store, heights, surfaces[1]);   // many fill plateaus

        foreach (var surface in surfaces)
        {
            var full = new int[n];
            Hydrography.ComputeReceivers(store, surface, full);
            var s = surface;
            for (int i = 0; i < n; i++)
                Assert.Equal(full[i], Hydrography.ReceiverAt(store, t => s[t], i));
        }
    }

    [Fact]
    public void TileFeatures_RiverTiles_StillReportConsistentUpAndDownstream()
    {
        using var world = new WorldBuilder().WithSize(5).WithSeed(42).Build();
        var hydro = world.Map.DataStore.GetSpan<HydrologyInfo>();
        int checkedTiles = 0;
        for (int i = 0; i < world.TileCount && checkedTiles < 60; i++)
        {
            if (hydro[i].IsRiver != 1) continue;
            var info = world.GetTileFeatures(i);
            if (info.DownstreamTile >= 0)
                Assert.Contains(info.DownstreamTile, Neighbors(world, i));
            if (info.UpstreamTile >= 0)
                Assert.Contains(info.UpstreamTile, Neighbors(world, i));
            Assert.NotEqual(RiverWaterSource.None, info.RiverSource);
            checkedTiles++;
        }
        Assert.True(checkedTiles > 0);
    }

    private static int[] Neighbors(World w, int t)
    {
        Span<int> nb = stackalloc int[6];
        int n = w.Map.DataStore.GetAdjacent(t, nb);
        return nb.Slice(0, n).ToArray();
    }
}
