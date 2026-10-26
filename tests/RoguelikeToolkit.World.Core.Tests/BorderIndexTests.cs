using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Queries;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X0 speed-ups must answer exactly what the scans they replace answered: the border index against
/// <see cref="HistoryNeighbors.Bordering"/> and a frontier scan, and the pruned <see cref="SpatialIndex.AnyWithin"/> against brute force.
/// </summary>
public class BorderIndexTests
{
    [Theory]
    [InlineData(1, 3)]
    [InlineData(4, 4)]
    public void BorderIndex_MatchesTheScans(int seed, int size)
    {
        using var world = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
        var store = world.Map.DataStore;
        int[] ownership = world.History.Ownership;
        var index = new BorderIndex(store, ownership);
        int polities = world.History.Polities.Count;
        Assert.True(polities > 2);
        Span<int> neighbors = stackalloc int[6];
        int pairs = 0;
        for (int a = 0; a < polities; a++)
        {
            Assert.Equal(HistoryNeighbors.Bordering(store, ownership, a), index.Bordering(a));
            for (int b = 0; b < polities; b++)
            {
                if (a == b) continue;
                var expected = new List<int>();
                for (int tile = 0; tile < store.TileCount; tile++)
                {
                    if (ownership[tile] != b) continue;
                    int adjacent = store.GetAdjacent(tile, neighbors);
                    for (int k = 0; k < adjacent; k++)
                        if (ownership[neighbors[k]] == a) { expected.Add(tile); break; }
                }
                Assert.Equal(expected, index.Frontier(a, b));
                if (expected.Count > 0) pairs++;
            }
        }
        Assert.True(pairs > 0);
        Assert.Empty(index.Bordering(-1));
        Assert.Empty(index.Frontier(polities + 5, 0));
    }

    [Fact]
    public void AnyWithin_MatchesBruteForce()
    {
        using var world = new WorldBuilder().WithSize(4).WithSeed(3).Build();
        var store = world.Map.DataStore;
        var vectors = store.GetTileVectors().ToArray();
        // A sparse mask, so many queries are true negatives and the pruning is exercised both ways.
        bool IsWater(int t) => t % 37 == 0;
        var index = new SpatialIndex(store, isWater: IsWater, isGlacier: _ => false);
        var rng = new Rng(11);
        int hits = 0, misses = 0;
        foreach (double km in new[] { 30.0, 150, 400, 1500, 9000, 19000 })
        {
            double cos = DetMath.Cos(km / World.EarthRadiusKm);
            for (int q = 0; q < 200; q++)
            {
                int from = (int)rng.NextUInt((uint)store.TileCount);
                bool expected = false;
                for (int t = 0; t < vectors.Length && !expected; t++)
                    expected = t != from && IsWater(t) && Vector3D.Dot(vectors[from], vectors[t]) >= cos;
                Assert.Equal(expected, index.AnyWithin(vectors[from], FeatureKind.WaterBody, cos, exceptTile: from));
                if (expected) hits++; else misses++;
            }
        }
        Assert.True(hits > 100 && misses > 100, $"hits {hits} misses {misses}");
    }
}
