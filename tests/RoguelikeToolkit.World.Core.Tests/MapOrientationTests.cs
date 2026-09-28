using System;
using Xunit;
using RoguelikeToolkit.World.Core;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// Seed-derivation determinism for <see cref="MapSeeds"/> and
/// gradient/strike sampling for <see cref="TerrainOrientation"/> on a
/// <see cref="WorldBuilder"/> world, including 5-ring pentagon tiles.
/// </summary>
public class MapOrientationTests
{
    /// <summary>Minimal <see cref="IHexMap{T}"/> over a square local grid.</summary>
    private sealed class LocalGridMap : IHexMap<LocalTile>
    {
        private readonly LocalTile[] _tiles;

        public LocalGridMap(int worldSeed, int worldTile, int cell, int size, GeoCoord center)
        {
            Size = size;
            uint seed = MapSeeds.DeriveLocalSeed(worldSeed, worldTile, cell);
#pragma warning disable CS0618 // Intentional: exercises the obsolete shim path.
            _tiles = RegionMaps.DeriveLocal(worldTile, cell, seed, size).Tiles;
#pragma warning restore CS0618
            RootAddress = MapAddress.ForLocal(worldSeed, worldTile, cell, -1);
            Bounds = MapBounds.ForGrid(center, 1.0, size);
        }

        public int Size { get; }
        public int TileCount => Size * Size;
        public MapAddress RootAddress { get; }
        public MapBounds Bounds { get; }

        public LocalTile GetTile(int index) => _tiles[index];

        public int GetAdjacent(int index, Span<int> neighbors)
        {
            var (q, r) = OffsetGrid.FromIndex(index, Size);
            int count = 0;
            if (q > 0 && count < neighbors.Length) neighbors[count++] = index - 1;
            if (q < Size - 1 && count < neighbors.Length) neighbors[count++] = index + 1;
            if (r > 0 && count < neighbors.Length) neighbors[count++] = index - Size;
            if (r < Size - 1 && count < neighbors.Length) neighbors[count++] = index + Size;
            return count;
        }

        public int GetTileAt(int q, int r) => OffsetGrid.ToIndex(q, r, Size);

        public int GetTileAt(CubeCoord cube)
        {
            if (!cube.IsValid) return -1;
            var (q, r) = cube.ToAxial();
            return GetTileAt(q, r);
        }
    }

    [Fact]
    public void MapSeeds_AreDeterministicAndTierSeparated()
    {
        Assert.Equal(MapSeeds.DeriveRegionSeed(7, 10), MapSeeds.DeriveRegionSeed(7, 10));
        Assert.Equal(MapSeeds.DeriveLocalSeed(7, 10, 3), MapSeeds.DeriveLocalSeed(7, 10, 3));
        Assert.Equal(
            MapSeeds.DeriveLocalTileSeed(7, 10, 3, 5),
            MapSeeds.DeriveLocalTileSeed(7, 10, 3, 5));

        // Distinct per-tier salts: same tile/cell must not alias across tiers.
        Assert.NotEqual(
            MapSeeds.DeriveRegionSeed(7, 10),
            MapSeeds.DeriveRegionCellSeed(7, 10, 0));
        Assert.NotEqual(
            MapSeeds.DeriveRegionCellSeed(7, 10, 3),
            MapSeeds.DeriveLocalSeed(7, 10, 3));

        var seen = new System.Collections.Generic.HashSet<uint>();
        for (int t = 0; t < 64; t++)
            seen.Add(MapSeeds.DeriveRegionSeed(7, t));
        Assert.True(seen.Count >= 60, $"Only {seen.Count}/64 distinct region seeds");
    }

    [Fact]
    public void TerrainOrientation_GradientAndStrike_AreNontrivialAndTangent()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        var store = world.Map.DataStore;
        var vectors = store.GetTileVectors();

        bool sawGradient = false;
        var hydro = store.GetSpan<HydrologyInfo>();
        Span<int> scratch = stackalloc int[6];
        Span<int> ring = stackalloc int[6];
        for (int tile = 0; tile < store.TileCount; tile++)
        {
            if (store.GetAdjacent(tile, scratch) != 6) continue; // hex tiles here
            var ctx = TerrainOrientation.Sample(store, tile);
            var again = TerrainOrientation.Sample(store, tile);

            // Deterministic.
            Assert.Equal(ctx.MeanElevation, again.MeanElevation);
            Assert.Equal(ctx.ElevationGradient, again.ElevationGradient);
            Assert.Equal(ctx.OrogenyStrike, again.OrogenyStrike);
            Assert.Equal(ctx.FlowEntryTile, again.FlowEntryTile);
            Assert.Equal(ctx.FlowExitTile, again.FlowExitTile);
            Assert.Equal(ctx.NeighborHeights, again.NeighborHeights);

            // Tangent to the sphere.
            var up = vectors[tile];
            Assert.True(Math.Abs(Vector3D.Dot(ctx.ElevationGradient, up)) < 1e-9);
            Assert.True(Math.Abs(Vector3D.Dot(ctx.OrogenyStrike, up)) < 1e-6);
            Assert.True(Math.Abs(ctx.OrogenyStrike.Length - 1.0) < 1e-6);
            Assert.InRange(ctx.AspectRadians, -Math.PI, Math.PI);

            // Drainage consistency on the routing surface: entry = highest
            // neighbor, exit = lowest neighbor when strictly below center.
            int ringCount = store.GetAdjacent(tile, ring);
            int expEntry = ring[0], expExit = ring[0];
            for (int k = 1; k < ringCount; k++)
            {
                if (hydro[ring[k]].Surface > hydro[expEntry].Surface) expEntry = ring[k];
                if (hydro[ring[k]].Surface < hydro[expExit].Surface) expExit = ring[k];
            }
            Assert.Equal(expEntry, ctx.FlowEntryTile);
            int expExitOrSink = hydro[expExit].Surface < hydro[tile].Surface ? expExit : -1;
            Assert.Equal(expExitOrSink, ctx.FlowExitTile);

            if (ctx.ElevationGradient.Length > 1e-6) sawGradient = true;
        }

        Assert.True(sawGradient, "Expected a nonzero elevation gradient somewhere on the world");
    }

    [Fact]
    public void TerrainOrientation_HandlesPentagonTiles()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        var store = world.Map.DataStore;

        int pentagon = -1;
        Span<int> scratch = stackalloc int[6];
        for (int i = 0; i < store.TileCount; i++)
        {
            if (store.GetAdjacent(i, scratch) == 5) { pentagon = i; break; }
        }
        Assert.True(pentagon >= 0, "Expected a pentagon tile");

        var ctx = TerrainOrientation.Sample(store, pentagon);
        Assert.Equal(5, ctx.NeighborHeights.Length);
        foreach (float h in ctx.NeighborHeights)
            Assert.True(float.IsFinite(h));
        Assert.True(double.IsFinite(ctx.AspectRadians));
        Assert.True(float.IsFinite(ctx.MeanElevation));
    }

    [Fact]
    public void HexMap_LocalGridAdapter_RoundTrips()
    {
        using var world = new WorldBuilder().WithSize(2).WithSeed(7).Build();
        var store = world.Map.DataStore;
        var center = store.GetGeoCoord(10);

        IHexMap<LocalTile> map = new LocalGridMap(7, 10, 3, 4, center);
        Assert.Equal(16, map.TileCount);
        Assert.Equal(MapTier.Local, map.RootAddress.Tier);
        Assert.Equal(4, map.Bounds.EdgeTiles.Length * 1 / 3); // 12 edge cells on a 4x4 grid

        for (int r = 0; r < 4; r++)
            for (int q = 0; q < 4; q++)
            {
                int index = map.GetTileAt(q, r);
                Assert.Equal(r * 4 + q, index);
                Assert.Equal(index, map.GetTileAt(CubeCoord.FromAxial(q, r)));
            }
        Assert.Equal(-1, map.GetTileAt(4, 0));
        Assert.Equal(-1, map.GetTileAt(new CubeCoord(1, 1, 1))); // x+y+z != 0

        Span<int> neighbors = stackalloc int[6];
        Assert.Equal(4, map.GetAdjacent(5, neighbors)); // interior cell of 4x4
        Assert.Equal(2, map.GetAdjacent(0, neighbors)); // corner cell
    }
}
