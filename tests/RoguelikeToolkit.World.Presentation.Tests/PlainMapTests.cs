using Xunit;
using RoguelikeToolkit.World.Presentation;
using RoguelikeToolkit.World.Core;
using System;

namespace RoguelikeToolkit.World.Presentation.Tests;

public class PlainMapTests
{
    private static readonly GeoCoord TestCenter = new(20, 30);

    private static RegionHandle TestRegion(int size = 8)
    {
        var up = Vector3D.FromGeoCoord(TestCenter);
        double lonR = TestCenter.Longitude * GeoCoord.Deg2Rad;
        var east = new Vector3D(-Math.Sin(lonR), Math.Cos(lonR), 0).Normalize();
        var north = Vector3D.Cross(up, east).Normalize();
        var parent = new ParentContext
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
        return RegionMaps.DeriveRegionMap(
            MapAddress.ForRegion(7, 10, -1), parent,
            TestCenter, 120.0, MapSeeds.DeriveRegionSeed(7, 10), size);
    }

    [Fact]
    public void FromRegion_CarriesLayoutGeoAndPlanetColors()
    {
        var region = TestRegion();
        var map = PlainMap.FromRegion(region);
        Assert.Equal(8, map.Size);
        Assert.Equal(64, map.Cells.Count);
        for (int i = 0; i < map.Cells.Count; i++)
        {
            var cell = map.Cells[i];
            Assert.Equal(i, cell.Index);
            Assert.Equal(FlatHexLayout.CellPos(i, 8), cell.Layout);
            Assert.Equal(6, cell.Corners.Length);
            Assert.Equal(region.CellCenter(i), cell.Geo);
            // Same call the planet Biome color mode uses: colors match the planet.
            Assert.Equal(BiomePalette.ColorFor(region.Cells[i].Biome), cell.Fill);
            Assert.Equal(region.CellAt(cell.Geo), i);
        }
    }

    [Fact]
    public void FromLocal_CarriesTileGeoAndPlanetColors()
    {
        var region = TestRegion();
        var local = region.GetLocal(3, 8);
        var map = PlainMap.FromLocal(local);
        Assert.Equal(8, map.Size);
        Assert.Equal(64, map.Cells.Count);
        for (int i = 0; i < map.Cells.Count; i++)
        {
            var cell = map.Cells[i];
            Assert.Equal(local.TileCenter(i), cell.Geo);
            Assert.Equal(BiomePalette.ColorFor(local.Tiles[i].Biome), cell.Fill);
            Assert.Equal(local.TileAt(cell.Geo), i);
        }
    }

    [Fact]
    public void FromLocal_RegionOverload_DerivesNamedCell()
    {
        var region = TestRegion();
        var map = PlainMap.FromLocal(region, 3);
        Assert.Equal(16 * 16, map.Cells.Count);
        Assert.Contains("cell 3", map.Title);
        Assert.Throws<IndexOutOfRangeException>(() => PlainMap.FromLocal(region, 64));
    }

    [Fact]
    public void HitTest_RoundTrips_AllCells()
    {
        var map = PlainMap.FromRegion(TestRegion());
        foreach (var cell in map.Cells)
            Assert.Equal(cell.Index, map.HitTest(cell.Layout.X, cell.Layout.Y));
    }

    [Fact]
    public void Corners_AreUnitHexesWithHexSpacing()
    {
        var map = PlainMap.FromRegion(TestRegion(4));
        foreach (var cell in map.Cells)
        {
            foreach (var v in cell.Corners)
            {
                double dx = v.X - cell.Layout.X, dy = v.Y - cell.Layout.Y;
                Assert.Equal(1.0, Math.Sqrt(dx * dx + dy * dy), 9);
            }
        }
        // Adjacent centers sit sqrt(3) apart (unit flat-top hexes).
        var a = map.Cells[0].Layout;
        var b = map.Cells[1].Layout;
        double d = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        Assert.Equal(Math.Sqrt(3), d, 9);
    }

    [Fact]
    public void PlainMaps_AreDeterministic()
    {
        var a = PlainMap.FromRegion(TestRegion());
        var b = PlainMap.FromRegion(TestRegion());
        for (int i = 0; i < a.Cells.Count; i++)
        {
            Assert.Equal(a.Cells[i].Geo, b.Cells[i].Geo);
            Assert.Equal(a.Cells[i].Fill, b.Cells[i].Fill);
            Assert.Equal(a.Cells[i].Layout, b.Cells[i].Layout);
        }
    }
}
