using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Tests;

public class DrainageOrderTests
{
    // The comparison the packed-key sort replaced, kept here as the specification.
    private static int[] Reference(float[] surface, int[] depth)
    {
        var order = Enumerable.Range(0, surface.Length).ToArray();
        Array.Sort(order, (a, b) =>
        {
            int c = surface[b].CompareTo(surface[a]);
            if (c != 0) return c;
            c = depth[b].CompareTo(depth[a]);
            return c != 0 ? c : a.CompareTo(b);
        });
        return order;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PackedSort_MatchesTheThreeWayComparison_OnTiedAndSignedValues(int seed)
    {
        var rng = new Random(seed);
        int n = 5000;
        var surface = new float[n];
        var depth = new int[n];
        for (int i = 0; i < n; i++)
        {
            // Few distinct levels (fill plateaus, lake surfaces), both signs of zero, a NaN or two.
            surface[i] = rng.Next(12) switch { 0 => 0f, 1 => -0f, 2 => float.NaN, var k => (k - 6) * 0.25f };
            depth[i] = rng.Next(4) == 0 ? rng.Next(300) : 0;
        }
        Assert.Equal(Reference(surface, depth), Hydrography.DrainageOrder(surface, depth));
    }

    [Fact]
    public void HeapKey_OrdersByLevelThenIndex()
    {
        var levels = new[] { 0.5f, -0f, 0f, -1f, 0.5f, float.PositiveInfinity, -3.5f };
        var keys = levels.Select((v, i) => (Key: TileOrdering.AscendingKey(v, i), Index: i)).OrderBy(x => x.Key).Select(x => x.Index).ToArray();
        var expected = Enumerable.Range(0, levels.Length).OrderBy(i => levels[i]).ThenBy(i => i).ToArray();
        Assert.Equal(expected, keys);
    }
}
