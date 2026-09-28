using System;
using System.Collections.Generic;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

public class SpatialIndexTests
{
    private static World BuildWorld() => new WorldBuilder().WithSize(2).WithSeed(7).Build();

    private static HashSet<int> ExpectedSet(World world, FeatureKind kind)
    {
        var store = world.Map.DataStore;
        var set = new HashSet<int>();
        switch (kind)
        {
            case FeatureKind.River:
                foreach (var r in world.Rivers.Rivers)
                    foreach (int t in r.Path) set.Add(t);
                break;
            case FeatureKind.WaterBody:
                var elev = store.GetSpan<ElevationInfo>();
                var hydro = store.GetSpan<HydrologyInfo>();
                for (int i = 0; i < store.TileCount; i++)
                    if (elev[i].Height < ElevationGenerationStage.SeaLevel || hydro[i].LakeDepth > 0f)
                        set.Add(i);
                foreach (var b in world.WaterBodies.Bodies)
                    foreach (int t in b.Tiles) set.Add(t);
                break;
            case FeatureKind.Range:
                foreach (var f in world.Ranges.Features)
                    foreach (int t in f.Tiles) set.Add(t);
                break;
            case FeatureKind.Glacier:
                var vectors = store.GetTileVectors();
                var climate = store.GetSpan<ClimateInfo>();
                var heights = store.GetSpan<ElevationInfo>();
                for (int i = 0; i < store.TileCount; i++)
                    if (Glaciology.IsGlacierTile(vectors[i], heights[i].Height, climate[i].Temperature, climate[i].Precipitation))
                        set.Add(i);
                break;
            case FeatureKind.Deposit:
                foreach (var d in world.Deposits.Deposits) set.Add(d.TileIndex);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
        return set;
    }

    private static (int Tile, double Dist)? ScanNearest(World world, GeoCoord from, HashSet<int> tiles)
    {
        var store = world.Map.DataStore;
        int best = -1;
        double bestD = double.MaxValue;
        foreach (int t in tiles)
        {
            double d = World.DistanceKm(from, store.GetGeoCoord(t));
            if (d < bestD) { bestD = d; best = t; }
        }
        return best < 0 ? null : (best, bestD);
    }

    [Fact]
    public void Index_AgreesWithScan_ForAllKinds()
    {
        using var world = BuildWorld();
        var store = world.Map.DataStore;
        var queries = new[]
        {
            store.GetGeoCoord(0), store.GetGeoCoord(10), store.GetGeoCoord(100),
            store.GetGeoCoord(store.TileCount - 1),
            new GeoCoord(0, 0), new GeoCoord(45, 90), new GeoCoord(-30, -120),
        };
        foreach (FeatureKind kind in Enum.GetValues<FeatureKind>())
        {
            if (kind == FeatureKind.Site) continue;
            var expected = ExpectedSet(world, kind);
            foreach (var q in queries)
            {
                var got = world.Index.NearestFeature(q, kind);
                var want = ScanNearest(world, q, expected);
                Assert.Equal(want == null, got == null);
                if (want == null) continue;
                // Exact nearest distance; the tile itself must achieve it (ties may pick either tile).
                Assert.True(Math.Abs(got!.Value.DistanceKm - want.Value.Dist) < 1e-6,
                    $"{kind} at ({q.Latitude},{q.Longitude}): index {got.Value.DistanceKm} vs scan {want.Value.Dist}");
                double back = World.DistanceKm(q, store.GetGeoCoord(got.Value.TileIndex));
                Assert.True(Math.Abs(back - want.Value.Dist) < 1e-6);
                Assert.Contains(got.Value.TileIndex, expected);
            }
        }
    }

    [Fact]
    public void NearestDeposit_WithType_AgreesWithScan()
    {
        using var world = BuildWorld();
        Assert.NotEmpty(world.Deposits.Deposits);
        var type = world.Deposits.Deposits[0].Type;
        var tiles = new HashSet<int>();
        foreach (var d in world.Deposits.Deposits)
            if (d.Type == type) tiles.Add(d.TileIndex);

        var store = world.Map.DataStore;
        foreach (int q in new[] { 0, 33, 120 })
        {
            var from = store.GetGeoCoord(q);
            var got = world.Index.NearestDeposit(from, type);
            var want = ScanNearest(world, from, tiles);
            Assert.NotNull(got);
            Assert.NotNull(want);
            Assert.True(Math.Abs(got!.Value.DistanceKm - want!.Value.Dist) < 1e-6);
        }
    }

    [Fact]
    public void WorldNearest_RewiredOntoIndex()
    {
        using var world = BuildWorld();
        var store = world.Map.DataStore;
        foreach (int q in new[] { 0, 10, 77, 150 })
        {
            var from = store.GetGeoCoord(q);
            Assert.Equal(world.Index.NearestFeature(from, FeatureKind.WaterBody), world.NearestWater(from));
            Assert.Equal(world.Index.NearestFeature(from, FeatureKind.River), world.NearestRiver(from));

            var dep = world.NearestDeposit(from);
            var hit = world.Index.NearestDeposit(from);
            Assert.Equal(hit == null, dep == null);
            if (hit != null)
            {
                Assert.Equal(hit.Value.TileIndex, dep!.Value.Deposit.TileIndex);
                Assert.Equal(hit.Value.DistanceKm, dep.Value.DistanceKm);
            }
        }
    }

    [Fact]
    public void AddressOverloads_AgreeWithCoordOverloads()
    {
        using var world = BuildWorld();
        var store = world.Map.DataStore;
        var addr = MapAddress.ForPlanet(world.Seed, 42);
        var coord = store.GetGeoCoord(42);
        Assert.Equal(
            world.Index.NearestFeature(coord, FeatureKind.River),
            world.Index.NearestFeature(addr, FeatureKind.River));
        Assert.Equal(
            world.Index.NearestDeposit(coord, null),
            world.Index.NearestDeposit(addr, null));
    }

    [Fact]
    public void NearestSite_FindsClosestOfKind()
    {
        using var world = BuildWorld();
        var store = world.Map.DataStore;
        var catalog = new SiteCatalog();
        catalog.Add(new PlacedSite { Kind = SiteKind.City, CellIndex = 5, InjectorId = "t" });
        catalog.Add(new PlacedSite { Kind = SiteKind.City, CellIndex = 50, InjectorId = "t" });
        catalog.Add(new PlacedSite { Kind = SiteKind.Ruin, CellIndex = 6, InjectorId = "t" });
        var index = new SpatialIndex(store, sites: catalog);

        var from = store.GetGeoCoord(0);
        var city = index.NearestSite(from, SiteKind.City);
        Assert.NotNull(city);
        double d5 = World.DistanceKm(from, store.GetGeoCoord(5));
        double d50 = World.DistanceKm(from, store.GetGeoCoord(50));
        Assert.Equal(Math.Min(d5, d50), city!.Value.DistanceKm, precision: 9);
        Assert.Equal(d5 <= d50 ? 5 : 50, city.Value.Site.CellIndex);

        var ruin = index.NearestSite(from, SiteKind.Ruin);
        Assert.NotNull(ruin);
        Assert.Equal(6, ruin!.Value.Site.CellIndex);

        Assert.Null(index.NearestSite(from, SiteKind.Mine));
        var siteTile = index.NearestFeature(from, FeatureKind.Site);
        Assert.NotNull(siteTile);

        var empty = new SpatialIndex(store);
        Assert.Null(empty.NearestSite(from, SiteKind.City));
        Assert.Null(empty.NearestFeature(from, FeatureKind.Site));
    }

    [Fact]
    public void Resolve_GetRegion_GetLocal_AgreeWithTileOverloads()
    {
        using var world = BuildWorld();
        var store = world.Map.DataStore;
        var coord = store.GetGeoCoord(25);
        var hex = world.Resolve(coord);
        Assert.Equal(world.Seed, hex.WorldSeed);
        Assert.Equal(store.GetTileIndex(coord), hex.TileIndex);

        var byTile = world.GetRegion(25);
        var byHex = world.GetRegion(hex);
        Assert.Equal(byTile.Seed, byHex.Seed);
        Assert.Equal(byTile.Cells.Length, byHex.Cells.Length);
        Assert.Equal(byTile.Cells[0].Seed, byHex.Cells[0].Seed);

        var localByHex = world.GetLocal(hex, 3);
        var localByHandle = byTile.GetLocal(3);
        Assert.Equal(localByHandle.Seed, localByHex.Seed);

        var localByRef = world.GetLocal(new RegionRef(world.Seed, 25, 3));
        Assert.Equal(localByHandle.Seed, localByRef.Seed);

        Assert.Throws<ArgumentException>(() => world.GetRegion(new PlanetHex(world.Seed + 1, 25)));
        var features = world.GetTileFeatures(25);
        Assert.Equal(MapAddress.ForPlanet(world.Seed, 25), features.Address);
    }
}
