using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>Territory follows the ground: a range is a frontier, not a line a plain hop count walks straight across.</summary>
public class TerritoryCostTests
{
    private static int[] Rings(WorldMap map, int start)
    {
        var store = map.DataStore;
        var ring = Enumerable.Repeat(-1, store.TileCount).ToArray();
        var q = new Queue<int>();
        ring[start] = 0; q.Enqueue(start);
        Span<int> nb = stackalloc int[6];
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            int a = store.GetAdjacent(c, nb);
            for (int k = 0; k < a; k++) if (ring[nb[k]] < 0) { ring[nb[k]] = ring[c] + 1; q.Enqueue(nb[k]); }
        }
        return ring;
    }

    [Fact]
    public void AMountainWall_HandsTheLandBehindItToTheOtherSide()
    {
        using var map = new WorldMap(4);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();
        var elev = map.DataStore.GetSpan<ElevationInfo>();
        const int a = 1000;
        var fromA = Rings(map, a);
        int b = Enumerable.Range(0, fromA.Length).First(i => fromA[i] == 12);
        var fromB = Rings(map, b);
        var behindWall = Enumerable.Range(0, fromA.Length).Where(i => fromA[i] is >= 4 and <= 5 && fromA[i] < fromB[i]).ToList();
        var capitals = new[] { a, b };
        var none = new Dictionary<int, int>();
        var costs = TerritoryCosts.FromPeoples(PeoplesStore.Load(), new[] { "human_common", "human_common" });

        for (int i = 0; i < elev.Length; i++) elev[i] = new ElevationInfo { Height = 0.1f };
        var flat = HistoryTerritory.AssignOwners(map.DataStore, capitals, none, costs);
        Assert.All(behindWall, t => Assert.Equal(0, flat[t])); // nearer A by hops: A's

        for (int i = 0; i < elev.Length; i++) if (fromA[i] == 3) elev[i] = new ElevationInfo { Height = 0.9f };
        var walled = HistoryTerritory.AssignOwners(map.DataStore, capitals, none, costs);
        int crossed = behindWall.Count(t => walled[t] == 1);
        Assert.True(crossed > behindWall.Count / 2, $"only {crossed}/{behindWall.Count} tiles behind the wall went to the other side");
        Assert.Equal(0, walled[a]);
        Assert.Equal(1, walled[b]);
    }

    [Fact]
    public void ShippedTerritoryData_NamesOnlyRealBiomesAndTerms()
    {
        var store = PeoplesStore.Load();
        Assert.True(store.Territories.ContainsKey("default"));
        var costs = TerritoryCosts.FromPeoples(store, store.Cultures.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
        Assert.Empty(costs.UnknownKeys);
    }

    [Fact]
    public void AMountainPeople_HoldsTheRangeAFlatlandPeopleCannot()
    {
        using var map = new WorldMap(4);
        map.RegisterLayer<ElevationInfo>(new ElevationLayer(map.DataStore));
        map.DataStore.Allocate();
        var elev = map.DataStore.GetSpan<ElevationInfo>();
        const int a = 1000;
        var fromA = Rings(map, a);
        int b = Enumerable.Range(0, fromA.Length).First(i => fromA[i] == 12);
        for (int i = 0; i < elev.Length; i++) elev[i] = new ElevationInfo { Height = fromA[i] is >= 3 and <= 9 ? 0.8f : 0.1f };
        var store = PeoplesStore.Load();
        int Held(string culture)
        {
            var owners = HistoryTerritory.AssignOwners(map.DataStore, new[] { a, b }, new Dictionary<int, int>(),
                TerritoryCosts.FromPeoples(store, new[] { culture, "human_common" }));
            return owners.Count(o => o == 0);
        }
        Assert.True(Held("dwarven_clan") > Held("human_common"), "dwarves should carry their reach further over high ground");
    }
}
