using Xunit;
using RoguelikeToolkit.World.Core;
using System;

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
    public void WorldDataStore_GetAdjacent_ReturnsFiveForPentagon()
    {
        using var store = new WorldDataStore(1);
        Span<int> neighbors = stackalloc int[6];
        int count = store.GetAdjacent(0, neighbors);

        Assert.Equal(5, count);
        Assert.Equal(1, neighbors[0]); // Testing dummy adjacency logic
    }

    [Fact]
    public void WorldDataStore_GetAdjacent_ReturnsSixForHexagon()
    {
        using var store = new WorldDataStore(2); // size 2 -> 42 tiles
        Span<int> neighbors = stackalloc int[6];
        int count = store.GetAdjacent(15, neighbors);

        Assert.Equal(6, count);
        Assert.Equal(16, neighbors[0]); // Testing dummy adjacency logic
    }

    [Fact]
    public void WorldDataStore_GetTileIndex_ReturnsValidIndex()
    {
        using var store = new WorldDataStore(2);
        var coord = new GeoCoord(45, 90);
        int index = store.GetTileIndex(coord);

        Assert.True(index >= 0);
        Assert.True(index < store.TileCount);
    }

    [Fact]
    public void WorldDataStore_Indexer_ThrowsIndexOutOfRangeException_WhenIndexIsOutOfBounds()
    {
        using var store = new WorldDataStore(1);

        // Accessing at TileCount should throw since valid indices are 0 to TileCount - 1
        Assert.Throws<IndexOutOfRangeException>(() => store.GetRef<DummyData>(store.TileCount));

        // Accessing negative index should also throw
        Assert.Throws<IndexOutOfRangeException>(() => store.GetRef<DummyData>(-1));
    }
}
