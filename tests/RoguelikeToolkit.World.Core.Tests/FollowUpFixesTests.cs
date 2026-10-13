using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Integration;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Regression tests for the Phase 6 findings backlog.</summary>
public class FollowUpFixesTests
{
    [Fact]
    public void QueryRadius_AppliesMaterializedBiomeOverride()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var center = world.Map.DataStore.GetGeoCoord(5);
        var store = new MaterializationStore();
        var target = world.QueryRadius(center, 1).First(h => h.TileIndex == 5).Biome == BiomeType.Plains ? BiomeType.Tundra : BiomeType.Plains;
        store.SetOverride(MapAddress.ForPlanet(world.Seed, 5), new TileOverride { Biome = target });

        Assert.Equal(target, world.QueryRadius(center, 1, new QueryOptions { Materialized = store }).First(h => h.TileIndex == 5).Biome);
        Assert.NotEqual(target, world.QueryRadius(center, 1).First(h => h.TileIndex == 5).Biome);
    }

    [Fact]
    public void CatalogPopulate_OnNonEmptyCatalog_ContinuesIds()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        int deposits = world.Deposits.Deposits.Count, features = world.Ranges.Features.Count;
        Assert.True(deposits > 0 && features > 0);

        DepositCatalogBuilder.Populate(world.Map, world.Deposits, world.Seed);
        RangeCatalogBuilder.Populate(world.Map, world.Ranges);

        Assert.Equal(2 * deposits, world.Deposits.Deposits.Count);
        // The builder populated the ranges at its river threshold scale and this call uses the default, so the canyon
        // count can differ by a feature or two between the two passes; what must hold is that ids continue.
        Assert.True(world.Ranges.Features.Count > features);
        Assert.Equal(world.Deposits.Deposits.Count, world.Deposits.Deposits.Select(d => d.Id).Distinct().Count());
        Assert.All(world.Ranges.Features.Select((f, i) => (f, i)), x => Assert.Equal(x.i, x.f.Id));
    }

    [Fact]
    public void GetRef_RejectsTables()
    {
        using var store = new WorldDataStore(1);
        store.RegisterLayer<TectonicPlate>();
        store.RegisterTable<PlateInfo>(7);
        store.Allocate();

        Assert.Throws<InvalidOperationException>(() => store.GetRef<PlateInfo>(0));
        Assert.Throws<IndexOutOfRangeException>(() => store.GetRef<TectonicPlate>(store.TileCount));
        store.GetRef<TectonicPlate>(0).Id = 3;
        Assert.Equal(3, store.GetSpan<TectonicPlate>()[0].Id);
    }

    [Fact]
    public void Erosion_ZeroThermalIterations_StillRunsOtherPasses_DisabledDoesNot()
    {
        using var a = new WorldBuilder().WithSize(4).WithSeed(42).WithErosion(e => e.Enabled = false).Build();
        using var b = new WorldBuilder().WithSize(4).WithSeed(42).WithErosion(e => e.Iterations = 0).Build();
        using var c = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var ha = a.Map.DataStore.GetSpan<ElevationInfo>().ToArray().Select(x => x.Height).ToArray();
        var hb = b.Map.DataStore.GetSpan<ElevationInfo>().ToArray().Select(x => x.Height).ToArray();
        var hc = c.Map.DataStore.GetSpan<ElevationInfo>().ToArray().Select(x => x.Height).ToArray();
        Assert.NotEqual(ha, hb);
        Assert.NotEqual(hb, hc);
    }

    [Fact]
    public void Wkt_EmptyGeometry_IsValidEmptyWkt()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(1).Build();
        var s = world.Map.DataStore;
        Assert.Equal("LINESTRING EMPTY", GeoWkt.RiverToWkt(new River(0), s));
        Assert.Equal("LINESTRING EMPTY", GeoWkt.RangeToWkt(new RangeFeature(0, RangeKind.MountainRange), s));
        Assert.Equal("POLYGON EMPTY", GeoWkt.WaterBodyToWkt(new WaterBody(0, WaterBodyKind.Lake), s));
    }

    [Fact]
    public void WaterBodyWkt_RingIsClosedAndAngularlyOrdered()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var s = world.Map.DataStore;
        foreach (var body in world.WaterBodies.Bodies.Where(b => (b.Boundary.Count > 0 ? b.Boundary.Count : b.Tiles.Count) >= 3))
        {
            var wkt = GeoWkt.WaterBodyToWkt(body, s);
            Assert.StartsWith("POLYGON((", wkt);
            var v = wkt["POLYGON((".Length..^2].Split(", ");
            Assert.Equal(v[0], v[^1]);
        }
    }
}
