using Xunit;
using RoguelikeToolkit.World.Core;
using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core.Tests;

public class AdjacencyTests
{
    private struct DummyData { public int value; }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(1, 42)]
    [InlineData(2, 162)]
    [InlineData(3, 642)]
    public void WorldDataStore_CalculatesCorrectTileCount(int size, int expected)
    {
        Assert.Equal(expected, WorldDataStore.GetTileCount(size));
    }

    [Fact]
    public void WorldTopology_IsSphericalManifold()
    {
        using var store = new WorldDataStore(2);

        int pentagonCount = 0;

        for (int i = 0; i < store.TileCount; i++)
        {
            Span<int> neighbors = stackalloc int[6];
            int count = store.GetAdjacent(i, neighbors);

            Assert.True(count == 5 || count == 6);
            if (count == 5)
            {
                pentagonCount++;
            }

            // Check spherical manifold property: every adjacent pair shares exactly 2 common neighbors
            for (int n = 0; n < count; n++)
            {
                int neighbor = neighbors[n];

                Span<int> neighborNeighbors = stackalloc int[6];
                int neighborCount = store.GetAdjacent(neighbor, neighborNeighbors);

                int sharedCount = 0;
                for (int x = 0; x < count; x++)
                {
                    for (int y = 0; y < neighborCount; y++)
                    {
                        if (neighbors[x] == neighborNeighbors[y])
                        {
                            sharedCount++;
                        }
                    }
                }

                Assert.Equal(2, sharedCount);
            }
        }

        Assert.Equal(12, pentagonCount);
    }

    [Fact]
    public void WorldDataStore_GetAdjacent_ReturnsFiveNeighborsForPentagonsAndSixForHexagons()
    {
        using var store = new WorldDataStore(2);

        int pentagonCount = 0;
        int hexagonCount = 0;
        for (int i = 0; i < store.TileCount; i++)
        {
            Span<int> neighbors = stackalloc int[6];
            int count = store.GetAdjacent(i, neighbors);

            if (count == 5)
            {
                pentagonCount++;
            }
            else if (count == 6)
            {
                hexagonCount++;
            }
        }

        Assert.Equal(12, pentagonCount);
        Assert.Equal(store.TileCount - 12, hexagonCount);
    }

    [Fact]
    public void WorldDataStore_GetAdjacent_ReturnsSymmetricInRangeUniqueNeighbors()
    {
        using var store = new WorldDataStore(3);

        for (int i = 0; i < store.TileCount; i++)
        {
            Span<int> neighbors = stackalloc int[6];
            int count = store.GetAdjacent(i, neighbors);
            var seen = new HashSet<int>();

            for (int n = 0; n < count; n++)
            {
                int neighbor = neighbors[n];
                Assert.InRange(neighbor, 0, store.TileCount - 1);
                Assert.NotEqual(i, neighbor);
                Assert.True(seen.Add(neighbor));

                Span<int> reverse = stackalloc int[6];
                int reverseCount = store.GetAdjacent(neighbor, reverse);
                bool found = false;
                for (int r = 0; r < reverseCount; r++)
                {
                    if (reverse[r] == i)
                    {
                        found = true;
                        break;
                    }
                }

                Assert.True(found);
            }
        }
    }


    [Fact]
    public void WorldDataStore_TopologyBuildsForLargerSize()
    {
        using var store = new WorldDataStore(5);

        Span<int> neighbors = stackalloc int[6];
        int count = store.GetAdjacent(store.TileCount - 1, neighbors);

        Assert.Equal(6, count);
    }


    [Fact]
    public void WorldDataStore_GetTileVectors_ReturnsOneVectorPerTile()
    {
        using var store = new WorldDataStore(2);

        var vectors = store.GetTileVectors();

        Assert.Equal(store.TileCount, vectors.Length);
    }

    [Fact]
    public void WorldDataStore_GetTileIndex_MapsCenterBackToSameTile()
    {
        using var store = new WorldDataStore(2);

        for (int i = 0; i < store.TileCount; i++)
        {
            var coord = store.GetGeoCoord(i);
            int index = store.GetTileIndex(coord);
            Assert.Equal(i, index);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void WorldDataStore_GetTileIndex_RoundTripsCenters_AndToleratesSmallOffsets(int size)
    {
        using var store = new WorldDataStore(size);

        for (int i = 0; i < store.TileCount; i++)
        {
            var center = store.GetGeoCoord(i);
            Assert.Equal(i, store.GetTileIndex(center));

            // Clicks land near — not exactly on — tile centers; small offsets must resolve identically.
            // (This is the contract GlControl.TryPickHex relies on after the ring-math fix.)
            if (Math.Abs(center.Latitude) < 89.0)
            {
                var jittered = new GeoCoord(center.Latitude + 0.1, center.Longitude + 0.1);
                Assert.Equal(i, store.GetTileIndex(jittered));
            }
        }
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(2, 2000)]
    [InlineData(3, 2000)]
    [InlineData(4, 1000)]
    [InlineData(5, 500)]
    [InlineData(6, 100)]
    public void WorldDataStore_GetTileIndex_MatchesExact_ForSampledPoints(int size, int samples)
    {
        using var store = new WorldDataStore(size);
        var r = Rng.Create(1234, size);

        for (int s = 0; s < samples; s++)
        {
            double u = r.NextDouble() * 2.0 - 1.0;
            double lat = Math.Asin(Math.Clamp(u, -1.0, 1.0)) * (180.0 / Math.PI);
            double lon = r.NextDouble() * 360.0 - 180.0;
            var coord = new GeoCoord(lat, lon);
            Assert.Equal(store.GetTileIndexExact(coord), store.GetTileIndex(coord));
        }

        // Fixed edge cases: poles (meridian convergence), dateline wrap, equator.
        var edgeCases = new GeoCoord[]
        {
            new(89.9, 0), new(89.9, 120), new(89.9, -120),
            new(-89.9, 0), new(-89.9, 45), new(-89.9, -90),
            new(0, 180), new(0, -180), new(0, 179.9),
            new(45, 180), new(-45, -180),
        };
        foreach (var coord in edgeCases)
            Assert.Equal(store.GetTileIndexExact(coord), store.GetTileIndex(coord));
    }

    [Fact]
    public void WorldDataStore_Indexer_ThrowsIndexOutOfRangeException_WhenIndexIsOutOfBounds()
    {
        using var store = new WorldDataStore(1);

        Assert.Throws<IndexOutOfRangeException>(() => store.GetRef<DummyData>(store.TileCount));
        Assert.Throws<IndexOutOfRangeException>(() => store.GetRef<DummyData>(-1));
    }
}
