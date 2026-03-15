using Xunit;
using RoguelikeToolkit.World.Core;
using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core.Tests;

public class AdjacencyTests
{
    private struct DummyData { public int value; }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    public void WorldDataStore_CalculatesCorrectTileCount(int size)
    {
        int expected = 10 * size * size + 2;
        Assert.Equal(expected, WorldDataStore.GetTileCount(size));
    }

    [Fact]
    public void WorldDataStore_GetAdjacent_ReturnsFiveNeighborsForPentagonsAndSixForHexagons()
    {
        using var store = new WorldDataStore(2);

        for (int i = 0; i < store.TileCount; i++)
        {
            Span<int> neighbors = stackalloc int[6];
            int count = store.GetAdjacent(i, neighbors);

            if (i < 12)
            {
                Assert.Equal(5, count);
            }
            else
            {
                Assert.Equal(6, count);
            }
        }
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

    [Fact]
    public void WorldDataStore_Indexer_ThrowsIndexOutOfRangeException_WhenIndexIsOutOfBounds()
    {
        using var store = new WorldDataStore(1);

        Assert.Throws<IndexOutOfRangeException>(() => store.GetRef<DummyData>(store.TileCount));
        Assert.Throws<IndexOutOfRangeException>(() => store.GetRef<DummyData>(-1));
    }
}
