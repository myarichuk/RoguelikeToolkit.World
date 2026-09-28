using System;
using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// B1 per-cell lon/lat API: CellCenter/TileCenter agree with derivation,
/// inverse lookup round-trips, results are deterministic, and region-cell
/// coordinates work with <see cref="World.GetTileFeatures(GeoCoord)"/>.
/// </summary>
public class CellCenterTests
{
    private static readonly GeoCoord TestCenter = new(20, 30);
    private const double TestRadiusKm = 120.0;
    private const int TestSize = 8;

    private static ParentContext TestParent()
    {
        var up = Vector3D.FromGeoCoord(TestCenter);
        double lonR = TestCenter.Longitude * GeoCoord.Deg2Rad;
        var east = new Vector3D(-Math.Sin(lonR), Math.Cos(lonR), 0).Normalize();
        var north = Vector3D.Cross(up, east).Normalize();
        return new ParentContext
        {
            MeanElevation = 0.3f,
            DominantBiome = BiomeType.Plains,
            MeanTemperature = 0.6f,
            MeanPrecipitation = 0.5f,
            MeanMoisture = 0.5f,
            ElevationGradient = north * -0.4,
            OrogenyStrike = east,
            NeighborHeights = new[] { 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f },
            Wind = north,
        };
    }

    private static RegionHandle TestRegion()
        => RegionMaps.DeriveRegionMap(
            MapAddress.ForRegion(7, 10, -1), TestParent(),
            TestCenter, TestRadiusKm, MapSeeds.DeriveRegionSeed(7, 10), TestSize);

    [Fact]
    public void Region_RoundTrip_AllCells()
    {
        var region = TestRegion();
        for (int i = 0; i < region.TileCount; i++)
            Assert.Equal(i, region.CellAt(region.CellCenter(i)));
    }

    [Fact]
    public void Region_JitteredLookup_StaysWithinHalfCellDiagonal()
    {
        var region = TestRegion();
        double boundKm = 2.0 * TestRadiusKm / TestSize; // generous half-diagonal
        for (int i = 0; i < region.TileCount; i++)
        {
            var c = region.CellCenter(i);
            var jittered = new GeoCoord(c.Latitude + 0.03, c.Longitude + 0.04);
            int found = region.CellAt(jittered);
            Assert.Equal(i, found);
            Assert.True(World.DistanceKm(c, region.CellCenter(found)) <= boundKm);
        }
    }

    [Fact]
    public void Region_CellCenter_MatchesDerivationCenter()
    {
        // DeriveLocalMapForCell centers the child map with the same projection;
        // the handle API must agree with it bit-for-bit.
        var region = TestRegion();
        for (int i = 0; i < region.TileCount; i++)
        {
            var local = region.GetLocal(i, 8);
            Assert.Equal(region.CellCenter(i).Latitude, local.Bounds.Center.Latitude);
            Assert.Equal(region.CellCenter(i).Longitude, local.Bounds.Center.Longitude);
        }
    }

    [Fact]
    public void Region_Centers_AreDeterministic()
    {
        var a = TestRegion();
        var b = TestRegion();
        for (int i = 0; i < a.TileCount; i++)
        {
            Assert.Equal(a.CellCenter(i), b.CellCenter(i));
            Assert.Equal(a.CellAt(a.CellCenter(i)), b.CellAt(b.CellCenter(i)));
        }
    }

    [Fact]
    public void Local_RoundTrip_AllTiles()
    {
        var local = TestRegion().GetLocal(3, 8);
        for (int i = 0; i < local.TileCount; i++)
            Assert.Equal(i, local.TileAt(local.TileCenter(i)));
    }

    [Fact]
    public void Local_TileCenter_MatchesDerivationCenter()
    {
        // LocalMapHandle bounds carry the derivation-time center; TileCenter
        // must reproduce the region-cell center it was derived from.
        var region = TestRegion();
        var local = region.GetLocal(5, 8);
        Assert.Equal(region.CellCenter(5).Latitude, local.Bounds.Center.Latitude);
        Assert.Equal(region.CellCenter(5).Longitude, local.Bounds.Center.Longitude);
    }

    [Fact]
    public void World_GetTileFeatures_WorksWithRegionCellCoord()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        var region = world.GetRegion(0);
        var c = region.CellCenter(10);
        var resolved = world.Resolve(c);
        Assert.Equal(resolved.TileIndex, world.GetTileFeatures(c).TileIndex);

        var local = region.GetLocal(10);
        var tc = local.TileCenter(5);
        var resolvedLocal = world.Resolve(tc);
        Assert.Equal(resolvedLocal.TileIndex, world.GetTileFeatures(tc).TileIndex);
    }

    [Fact]
    public void CellCenter_OutOfRange_Throws()
    {
        var region = TestRegion();
        Assert.Throws<IndexOutOfRangeException>(() => region.CellCenter(-1));
        Assert.Throws<IndexOutOfRangeException>(() => region.CellCenter(region.TileCount));
        var local = region.GetLocal(0, 8);
        Assert.Throws<IndexOutOfRangeException>(() => local.TileCenter(-1));
        Assert.Throws<IndexOutOfRangeException>(() => local.TileCenter(local.TileCount));
    }
}
