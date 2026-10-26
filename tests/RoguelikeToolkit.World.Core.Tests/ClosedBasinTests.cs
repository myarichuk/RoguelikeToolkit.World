using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// A closed lake's basin keeps its water. The routing surface there is a plateau at the pour level, and resolving that
/// flat by distance sent the part of the basin nearest the pour point over it, towards the sea.
/// </summary>
public class ClosedBasinTests
{
    private static int[] RingsFrom(WorldMap map, int start)
    {
        var store = map.DataStore;
        var ring = Enumerable.Repeat(-1, store.TileCount).ToArray();
        var queue = new Queue<int>();
        ring[start] = 0;
        queue.Enqueue(start);
        Span<int> nb = stackalloc int[6];
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            int adjacent = store.GetAdjacent(cur, nb);
            for (int k = 0; k < adjacent; k++)
                if (ring[nb[k]] < 0) { ring[nb[k]] = ring[cur] + 1; queue.Enqueue(nb[k]); }
        }
        return ring;
    }

    [Fact]
    public void DryBasinOfAClosedLake_DrainsIntoTheLake()
    {
        // A wide funnel (rings 0..8) behind a rim at 0.30 with one notch at 0.25, outside slopes running down to the sea.
        using var map = new WorldMap(4);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();
        var store = map.DataStore;
        const int pit = 1000;
        var ring = RingsFrom(map, pit);
        int n = store.TileCount;
        var heights = new float[n];
        for (int i = 0; i < n; i++)
            heights[i] = ring[i] <= 8 ? 0.06f + 0.02f * ring[i] : ring[i] == 9 ? 0.30f : 0.24f - 0.02f * (ring[i] - 10);
        int notch = Enumerable.Range(0, n).First(i => ring[i] == 9);
        heights[notch] = 0.25f;

        var filled = new float[n];
        Hydrography.ComputeFilledSurface(store, heights, filled);
        var recvFilled = new int[n];
        var depthFilled = new int[n];
        Hydrography.ComputeReceivers(store, filled, recvFilled, depthFilled);
        var runoff = Enumerable.Repeat(0.2f, n).ToArray();
        var evap = Enumerable.Repeat(1.0f, n).ToArray();
        var flowFilled = new float[n];
        Hydrography.AccumulateFlow(store, Hydrography.DrainageOrder(filled, depthFilled), recvFilled, runoff, flowFilled);

        var lakes = LakeSolver.Solve(store, heights, filled, flowFilled, recvFilled, runoff, evap);
        var lake = Assert.Single(lakes.Lakes);
        Assert.False(lake.IsOpen);
        var basin = Enumerable.Range(0, n).Where(i => ring[i] <= 8).ToList();
        var dry = basin.Where(i => lakes.Depth[i] <= 0f).ToList();
        Assert.NotEmpty(dry);
        // The lake is fed by its whole basin, not just the rain that falls on the water.
        Assert.True(lake.Supply >= basin.Sum(i => runoff[i]) - 1e-3f, $"supply {lake.Supply} < basin runoff {basin.Sum(i => runoff[i])}");

        // The plateau resolved by distance alone (what the routing did before) lets part of the basin out.
        var receiver = new int[n];
        Hydrography.ComputeReceivers(store, lakes.Surface, receiver);
        Assert.Contains(dry, t => !EndsInLake(t, receiver, lakes, n));

        // The basin tree takes every dry basin tile down to the shore, one hop closer each step.
        foreach (int t in dry)
        {
            Assert.True(lakes.BasinReceiver[t] >= 0, $"tile {t} has no basin receiver");
            Assert.Equal(lakes.BasinDepth[t] - 1, lakes.BasinDepth[lakes.BasinReceiver[t]]);
            for (int i = 0; i < n; i++) if (lakes.BasinReceiver[i] >= 0) receiver[i] = lakes.BasinReceiver[i];
            Assert.True(EndsInLake(t, receiver, lakes, n), $"tile {t} does not reach the lake");
        }
    }

    private static bool EndsInLake(int t, int[] receiver, LakeSolution lakes, int n)
    {
        for (int steps = 0; steps < n && t >= 0; steps++)
        {
            if (lakes.LakeId[t] >= 0) return true;
            t = receiver[t];
        }
        return false;
    }
}
