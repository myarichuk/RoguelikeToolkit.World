using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

public class TradeRoadOperatorTests
{
    private static World Build(int seed) => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();

    private static void Run(World w)
        => new HistoryDriver().AddOperator(new TradeRoadOperator()).Apply(w.Map, w.History, new HistoryContext());

    [Fact]
    public void Roads_AreWellFormedRoutes_AndSameSeedSameResult()
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            using var a = Build(seed);
            using var b = Build(seed);
            Run(a);
            Run(b);
            Assert.Equal(a.History.Roads.Count, b.History.Roads.Count);
            foreach (var road in a.History.Roads)
            {
                Assert.True(road.RouteA < road.RouteB);
                Assert.InRange(road.FoundedTurn, 1, a.History.SimulatedTurns);
            }
            var routes = a.History.Roads.Select(r => (r.RouteA, r.RouteB)).Distinct().Count();
            Assert.Equal(routes, a.History.Events.Count(e => e.Kind == HistoryEventKind.RoadBuilt));
        }
    }

    [Fact]
    public void Spurs_ConnectSettlementsOfOnePolity()
    {
        int spurs = 0;
        for (int seed = 1; seed <= 6; seed++)
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory(c => c.HazardRate = 0).Build();
            var standingTiles = w.History.Settlements.ToDictionary(s => s.TileIndex, s => s.PolityId);
            foreach (var route in w.History.Roads.Where(r => r.PolityId >= 0).GroupBy(r => (r.RouteA, r.RouteB)))
            {
                spurs++;
                int owner = route.First().PolityId;
                Assert.Equal(owner, standingTiles[route.Key.RouteA]);
                Assert.Equal(owner, standingTiles[route.Key.RouteB]);
            }
        }
        Assert.True(spurs > 0);
    }

    [Fact]
    public void Gate_UsesRelativeGrainShare()
    {
        int[] farm = { 100, 20, 0, 20 };
        int[] herd = { 30, 80, 0, 80 };
        Assert.True(HistoryDemography.AreComplementary(farm, herd));
        Assert.True(HistoryDemography.AreComplementary(herd, farm));
        Assert.False(HistoryDemography.AreComplementary(farm, farm));
        Assert.False(HistoryDemography.AreComplementary(farm, new[] { 0, 0, 0, 0 }));
        Assert.False(HistoryDemography.AreComplementary(null, farm));
    }

    [Fact]
    public void DefaultWorlds_OpenRoutes()
    {
        int worldsWithRoutes = 0;
        for (int seed = 1; seed <= 6; seed++)
        {
            using var w = Build(seed);
            Run(w);
            if (w.History.Roads.Count > 0) worldsWithRoutes++;
        }
        Assert.True(worldsWithRoutes > 0);
    }
}
