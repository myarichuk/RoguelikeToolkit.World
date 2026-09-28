using System;
using System.Collections.Generic;
using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Focused coverage for the hex-grid <see cref="RegionMaps"/> rework:
/// determinism, strike alignment, edge stitching, and river continuity.
/// </summary>
public class RegionMapsTests
{
    private static readonly GeoCoord TestCenter = new(20, 30);
    private const double TestRadiusKm = 120.0;

    private static ParentContext TestParent(bool river = false, float flow = 0f)
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
            AspectRadians = 0.0,
            OrogenyStrike = east,
            NeighborHeights = new[] { 0.3f, 0.3f, 0.3f, 0.3f, 0.3f, 0.3f },
            FlowEntryTile = -1,
            FlowExitTile = -1,
            Wind = north,
            IsRiver = river,
            Flow = flow,
        };
    }

    private static RegionHandle TestRegion(bool river = false, float flow = 0f, int size = 8)
        => RegionMaps.DeriveRegionMap(
            MapAddress.ForRegion(7, 10, -1), TestParent(river, flow),
            TestCenter, TestRadiusKm, MapSeeds.DeriveRegionSeed(7, 10), size);

    [Fact]
    public void RegionAndLocal_AreDeterministic()
    {
        var a = TestRegion();
        var b = TestRegion();
        Assert.Equal(a.Seed, b.Seed);
        Assert.Equal(a.Cells.Length, b.Cells.Length);
        for (int i = 0; i < a.Cells.Length; i++)
        {
            Assert.Equal(a.Cells[i].Seed, b.Cells[i].Seed);
            Assert.Equal(a.Cells[i].Elevation, b.Cells[i].Elevation);
            Assert.Equal(a.Cells[i].Biome, b.Cells[i].Biome);
            Assert.Equal(a.Cells[i].Moisture, b.Cells[i].Moisture);
            Assert.Equal(a.Cells[i].IsRiver, b.Cells[i].IsRiver);
            Assert.Equal(a.Cells[i].WaterDepth, b.Cells[i].WaterDepth);
            Assert.Equal(a.Cells[i].Flow, b.Cells[i].Flow);
        }

        var la = a.GetLocal(3);
        var lb = b.GetLocal(3);
        Assert.Equal(la.Seed, lb.Seed);
        Assert.Equal(la.Tiles.Length, lb.Tiles.Length);
        for (int i = 0; i < la.Tiles.Length; i++)
        {
            Assert.Equal(la.Tiles[i].Height, lb.Tiles[i].Height);
            Assert.Equal(la.Tiles[i].Biome, lb.Tiles[i].Biome);
            Assert.Equal(la.Tiles[i].Temperature, lb.Tiles[i].Temperature);
            Assert.Equal(la.Tiles[i].Precipitation, lb.Tiles[i].Precipitation);
            Assert.Equal(la.Tiles[i].WaterDepth, lb.Tiles[i].WaterDepth);
        }
    }

    [Fact]
    public void HexGrid_HasHexAdjacencyAndAddressing()
    {
        var region = TestRegion();
        IHexMap<RegionCell> map = region;
        Assert.Equal(64, map.TileCount);
        Assert.Equal(MapTier.Region, map.RootAddress.Tier);
        Assert.Equal(7, map.RootAddress.WorldSeed);

        for (int r = 0; r < 8; r++)
            for (int q = 0; q < 8; q++)
            {
                int index = map.GetTileAt(q, r);
                Assert.Equal(r * 8 + q, index);
                Assert.Equal(index, map.GetTileAt(CubeCoord.FromAxial(q, r)));
            }
        Assert.Equal(-1, map.GetTileAt(8, 0));
        Assert.Equal(-1, map.GetTileAt(new CubeCoord(1, 1, 1)));

        Span<int> neighbors = stackalloc int[6];
        int interior = map.GetTileAt(4, 4);
        Assert.Equal(6, map.GetAdjacent(interior, neighbors));
        Assert.Equal(2, map.GetAdjacent(0, neighbors)); // corner of the patch

        var local = region.GetLocal(3, 8);
        IHexMap<LocalTile> lmap = local;
        Assert.Equal(MapTier.Local, lmap.RootAddress.Tier);
        Assert.Equal(3, lmap.RootAddress.RegionCellIndex);
        Assert.Equal(local.Parent.DominantBiome, local.Tiles[0].ParentBiome);
    }

    /// <summary>
    /// Mean neighbor height step along vs across a strike direction (pixel
    /// frame). Interior pairs only: edge pinning damps detail at the border.
    /// Returns across/along (crest signal when &gt; 1).
    /// </summary>
    private static double StrikeRoughnessRatio(int size, Func<int, float> height, double strikeX, double strikeY)
    {
        double alongSum = 0, acrossSum = 0;
        int alongN = 0, acrossN = 0;
        Span<int> nb = stackalloc int[6];
        bool Deep(int i)
        {
            var (q, r) = OffsetGrid.FromIndex(i, size);
            return Math.Min(Math.Min(q, size - 1 - q), Math.Min(r, size - 1 - r)) >= 1;
        }
        for (int i = 0; i < size * size; i++)
        {
            if (!Deep(i)) continue;
            var (iq, ir) = OffsetGrid.FromIndex(i, size);
            int m = RegionMaps.GetHexAdjacent(i, size, nb);
            for (int k = 0; k < m; k++)
            {
                int j = nb[k];
                if (j <= i || !Deep(j)) continue; // each unordered pair once
                var (jq, jr) = OffsetGrid.FromIndex(j, size);
                double dx = 1.5 * (jq - iq);
                double dy = 1.7320508075688772 * ((jq - iq) * 0.5 + (jr - ir));
                double len = Math.Sqrt(dx * dx + dy * dy);
                double align = Math.Abs((dx * strikeX + dy * strikeY) / len);
                double dh = Math.Abs(height(i) - height(j));
                if (align > 0.5) { alongSum += dh; alongN++; }
                else { acrossSum += dh; acrossN++; }
            }
        }
        return (acrossSum / Math.Max(acrossN, 1)) / (alongSum / Math.Max(alongN, 1));
    }

    private static ParentContext ParentWithStrike(Vector3D strike)
    {
        var parent = TestParent();
        return new ParentContext
        {
            MeanElevation = parent.MeanElevation,
            DominantBiome = parent.DominantBiome,
            MeanTemperature = parent.MeanTemperature,
            MeanPrecipitation = parent.MeanPrecipitation,
            MeanMoisture = parent.MeanMoisture,
            ElevationGradient = parent.ElevationGradient,
            AspectRadians = parent.AspectRadians,
            OrogenyStrike = strike,
            NeighborHeights = parent.NeighborHeights,
            FlowEntryTile = parent.FlowEntryTile,
            FlowExitTile = parent.FlowExitTile,
            Wind = parent.Wind,
            IsRiver = parent.IsRiver,
            Flow = parent.Flow,
        };
    }

    [Fact]
    public void RegionDetail_FollowsParentStrike()
    {
        // The alignment must follow the parent strike (not a baked-in axis):
        // east and north strikes, several seeds. Deterministic: fixed inputs.
        var up = Vector3D.FromGeoCoord(TestCenter);
        double lonR = TestCenter.Longitude * GeoCoord.Deg2Rad;
        var east = new Vector3D(-Math.Sin(lonR), Math.Cos(lonR), 0).Normalize();
        var north = Vector3D.Cross(up, east).Normalize();
        uint[] seeds = { MapSeeds.DeriveRegionSeed(7, 10), 12345u, 999u, 424242u };
        foreach (uint seed in seeds)
        {
            var re = RegionMaps.DeriveRegionMap(
                MapAddress.ForRegion(7, 10, -1), ParentWithStrike(east),
                TestCenter, TestRadiusKm, seed);
            double ratioE = StrikeRoughnessRatio(re.Size, i => re.Cells[i].Elevation, 1, 0);
            Assert.True(ratioE > 1.08, $"seed={seed} strike=E ratio={ratioE}");

            var rn = RegionMaps.DeriveRegionMap(
                MapAddress.ForRegion(7, 10, -1), ParentWithStrike(north),
                TestCenter, TestRadiusKm, seed);
            double ratioN = StrikeRoughnessRatio(rn.Size, i => rn.Cells[i].Elevation, 0, 1);
            Assert.True(ratioN > 1.08, $"seed={seed} strike=N ratio={ratioN}");
        }
    }

    [Fact]
    public void LocalDetail_FollowsParentStrikeLoosely()
    {
        // Real path: region map, child context, local map. The child inherits
        // the parent strike; the looser local warp still aligns crests to it.
        var region = TestRegion();
        foreach (int cell in new[] { 10, 27, 36 })
        {
            var local = region.GetLocal(cell);
            var child = local.Parent;
            var center = region.Bounds.Center;
            var up = Vector3D.FromGeoCoord(center);
            double lonR = center.Longitude * GeoCoord.Deg2Rad;
            var east = new Vector3D(-Math.Sin(lonR), Math.Cos(lonR), 0).Normalize();
            var north = Vector3D.Cross(up, east).Normalize();
            double sx = Vector3D.Dot(child.OrogenyStrike, east);
            double sy = Vector3D.Dot(child.OrogenyStrike, north);
            double len = Math.Sqrt(sx * sx + sy * sy);
            double ratio = StrikeRoughnessRatio(local.Size, i => local.Tiles[i].Height, sx / len, sy / len);
            Assert.True(ratio > 1.1, $"cell={cell} ratio={ratio}");
        }
    }

    [Fact]
    public void EdgeCells_ArePinnedToParentBoundary()
    {
        var parent = TestParent();
        var region = TestRegion();
        double maxEdgeMismatch = 0;
        double maxInteriorDetail = 0;
        for (int i = 0; i < region.Cells.Length; i++)
        {
            var (q, r) = OffsetGrid.FromIndex(i, region.Size);
            var (u, v) = RegionMaps.CellUV(q, r, region.Size);
            double expected = RegionMaps.ParentBaseElevation(parent, TestCenter, TestRadiusKm, u, v);
            double mismatch = Math.Abs(region.Cells[i].Elevation - expected);
            bool edge = q == 0 || r == 0 || q == region.Size - 1 || r == region.Size - 1;
            if (edge) maxEdgeMismatch = Math.Max(maxEdgeMismatch, mismatch);
            else maxInteriorDetail = Math.Max(maxInteriorDetail, mismatch);
        }
        Assert.True(maxEdgeMismatch < 0.02, $"edge mismatch {maxEdgeMismatch}");
        // Non-vacuous: the interior actually carries detail.
        Assert.True(maxInteriorDetail > 0.03, $"interior detail {maxInteriorDetail}");
    }

    [Fact]
    public void RegionRiver_ThreadsEntryToExit()
    {
        var region = TestRegion(river: true);
        Assert.True(region.HasRiver);
        Assert.True(region.RiverEntryCell >= 0);
        Assert.True(region.RiverExitCell >= 0);
        Assert.True(region.Cells[region.RiverEntryCell].IsRiver);
        Assert.True(region.Cells[region.RiverExitCell].IsRiver);

        var path = ConnectedRiverPath(region.Size, region.RiverEntryCell, region.RiverExitCell,
            i => region.Cells[i].IsRiver);
        Assert.NotNull(path);

        var calm = TestRegion();
        Assert.False(calm.HasRiver);
    }

    [Fact]
    public void LocalRiver_ThreadsEntryToExit()
    {
        var region = TestRegion(river: true);
        int riverCell = Array.FindIndex(region.Cells, c => c.IsRiver);
        Assert.True(riverCell >= 0);

        var local = region.GetLocal(riverCell, 8);
        Assert.True(local.HasRiver);
        Assert.True(local.Tiles[local.RiverEntryCell].WaterDepth >= RegionMaps.LocalRiverMark);
        Assert.True(local.Tiles[local.RiverExitCell].WaterDepth >= RegionMaps.LocalRiverMark);

        var path = ConnectedRiverPath(local.Size, local.RiverEntryCell, local.RiverExitCell,
            i => local.Tiles[i].WaterDepth >= RegionMaps.LocalRiverMark);
        Assert.NotNull(path);
    }

    private static List<int>? ConnectedRiverPath(int size, int entry, int exit, Func<int, bool> isRiver)
    {
        // BFS over river cells; returns a path when entry connects to exit.
        var prev = new int[size * size];
        Array.Fill(prev, -2);
        var queue = new Queue<int>();
        queue.Enqueue(entry);
        prev[entry] = -1;
        Span<int> nb = stackalloc int[6];
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (cur == exit)
            {
                var path = new List<int>();
                for (int c = cur; c >= 0; c = prev[c]) path.Add(c);
                path.Reverse();
                return path;
            }
            int m = RegionMaps.GetHexAdjacent(cur, size, nb);
            for (int k = 0; k < m; k++)
            {
                int j = nb[k];
                if (prev[j] != -2 || !isRiver(j)) continue;
                prev[j] = cur;
                queue.Enqueue(j);
            }
        }
        return null;
    }
}
