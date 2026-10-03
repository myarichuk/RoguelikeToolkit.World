using System;
using RoguelikeToolkit.World.Core;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Third review round: seed derivation, bounds equality, structural refactors.</summary>
public class ReviewFixesRound3Tests
{
    [Fact]
    public void LocalSeeds_NoLongerCollideOnLinearlyRelatedAddresses()
    {
        // The old pre-mix was tile*7919 + cell*104729, so (104729, 0) and (0, 7919)
        // produced the very same seed.
        const int world = 1234;
        Assert.NotEqual(MapSeeds.DeriveLocalSeed(world, 104729, 0), MapSeeds.DeriveLocalSeed(world, 0, 7919));
        Assert.NotEqual(MapSeeds.DeriveRegionCellSeed(world, 104729, 0), MapSeeds.DeriveRegionCellSeed(world, 0, 7919));
        Assert.NotEqual(
            MapSeeds.DeriveLocalTileSeed(world, 104729, 0, 0),
            MapSeeds.DeriveLocalTileSeed(world, 0, 7919, 0));
    }

    [Fact]
    public void TierStreams_AreIndependentAcrossWorldsAndTiers()
    {
        // (worldSeed ^ salt) used to alias a tier of one world onto another tier of a different world.
        int a = 77;
        int b = a ^ MapSeeds.RegionSalt ^ MapSeeds.LocalSalt;
        Assert.NotEqual(MapSeeds.DeriveRegionSeed(a, 5), MapSeeds.DeriveLocalSeed(b, 5, 0));
        Assert.NotEqual(MapSeeds.DeriveRegionSeed(a, 5), MapSeeds.DeriveRegionSeed(a, 6));
        Assert.Equal(MapSeeds.DeriveLocalSeed(a, 5, 3), MapSeeds.DeriveLocalSeed(a, 5, 3));
    }

    [Fact]
    public void InjectorRng_DistinguishesLinearlyRelatedAddresses()
    {
        // Old mix: worldSeed*7919 + tile*104729 -> (104729, 0) == (0, 7919).
        var x = SiteInjectorPipeline.DeriveInjectorRng(MapAddress.ForRegion(104729, 0, -1), "ruin");
        var y = SiteInjectorPipeline.DeriveInjectorRng(MapAddress.ForRegion(0, 7919, -1), "ruin");
        Assert.NotEqual(x.NextULong(), y.NextULong());

        var other = SiteInjectorPipeline.DeriveInjectorRng(MapAddress.ForRegion(0, 7919, -1), "mine");
        var same = SiteInjectorPipeline.DeriveInjectorRng(MapAddress.ForRegion(0, 7919, -1), "ruin");
        Assert.NotEqual(other.NextULong(), same.NextULong());
    }

    [Fact]
    public void MapBounds_EqualityComparesEdgeRingByContent()
    {
        var center = new GeoCoord(10, 20);
        var a = MapBounds.ForGrid(center, 50.0, 5);
        var b = MapBounds.ForGrid(center, 50.0, 5);
        Assert.NotSame(a.EdgeTiles, b.EdgeTiles);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a == b);

        Assert.NotEqual(a, MapBounds.ForGrid(center, 50.0, 6));
        Assert.NotEqual(a, MapBounds.ForGrid(center, 51.0, 5));
        Assert.NotEqual(a, MapBounds.ForGrid(new GeoCoord(11, 20), 50.0, 5));
    }

    [Fact]
    public void LandCellsByElevation_IsDescendingWithLowestIndexTies()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var region = world.GetRegion(7);
        var ctx = new SiteInjectionContext(
            region.Address, SiteTier.Region, region.Parent, region.Bounds, region, null,
            Array.Empty<PlacedSite>(), new Rng(1));
        var land = ctx.LandCellsByElevationDescending();
        for (int k = 1; k < land.Length; k++)
        {
            float prev = ctx.GetElevation(land[k - 1]), cur = ctx.GetElevation(land[k]);
            Assert.True(prev > cur || (prev == cur && land[k - 1] < land[k]));
        }
        foreach (int i in land) Assert.False(ctx.IsWater(i));
        int landCount = 0;
        for (int i = 0; i < ctx.CellCount; i++) if (!ctx.IsWater(i)) landCount++;
        Assert.Equal(landCount, land.Length);
    }

    [Fact]
    public void Handles_ShareGridBehavior()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(42).Build();
        var region = world.GetRegion(7);
        var local = region.GetLocal(10);
        Assert.IsAssignableFrom<IHexMap<RegionCell>>(region);
        Assert.IsAssignableFrom<IHexMap<LocalTile>>(local);
        Assert.Equal(region.Size * region.Size, region.TileCount);
        Assert.Equal(local.Size * local.Size, local.TileCount);
        Assert.Equal(10, region.CellAt(region.CellCenter(10)));
        Assert.Equal(37, local.TileAt(local.TileCenter(37)));
        Assert.Equal(-1, region.GetTileAt(-1, 0));
        Assert.Equal(11, region.GetTileAt(CubeCoord.FromAxial(3, 1)));
    }
}
