using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

public class TradeRoadOperatorTests
{
    private static World Build(int seed) => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory().Build();

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
                Assert.Equal(-1, road.PolityId);
                Assert.True(road.RouteA < road.RouteB);
                Assert.InRange(road.FoundedTurn, 1, a.History.SimulatedTurns);
            }
            var routes = a.History.Roads.Select(r => (r.RouteA, r.RouteB)).Distinct().Count();
            Assert.Equal(routes, a.History.Events.Count(e => e.Kind == HistoryEventKind.RoadBuilt));
        }
    }
}
