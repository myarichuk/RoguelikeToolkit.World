using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

public class WarOperatorTests
{
    private static World Build(int seed) => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory().Build();

    private static void Run(World w)
        => new HistoryDriver()
            .AddOperator(new PopulationOperator())
            .AddOperator(new HolySiteOperator())
            .AddOperator(new TradeRoadOperator())
            .AddOperator(new WarOperator())
            .Apply(w.Map, w.History, new HistoryContext());

    [Fact]
    public void Wars_AreWellFormed_AndSameSeedSameResult()
    {
        int wars = 0;
        for (int seed = 1; seed <= 8; seed++)
        {
            using var a = Build(seed);
            using var b = Build(seed);
            Run(a);
            Run(b);
            var h = a.History;
            var aw = h.Events.Where(e => e.Kind == HistoryEventKind.War).ToList();
            Assert.Equal(aw.Count, b.History.Events.Count(e => e.Kind == HistoryEventKind.War));
            foreach (var war in aw)
            {
                wars++;
                int defender = int.Parse(war.Tags.First(t => t.StartsWith("defender:")).Substring(9));
                Assert.NotEqual(war.PolityId, defender);
                Assert.InRange(war.Turn, 1, h.SimulatedTurns);
                Assert.True(h.Sites.Any(s => s.TileIndex == war.TileIndex));
            }
            foreach (var cut in h.Events.Where(e => e.Kind == HistoryEventKind.RoadCut))
                Assert.True(h.Roads.Any(r => r.Severed));
            foreach (var g in h.Grudges) Assert.True(g.LastTurn >= g.FirstTurn);
        }
        Assert.True(wars > 0, "No war across seeds 1-8.");
    }

    [Fact]
    public void BattlefieldOnHolyTile_WhenItOccurs_IsTiedToAWarOnThatTile()
    {
        int desecrations = 0;
        for (int seed = 1; seed <= 20 && desecrations == 0; seed++)
        {
            using var w = Build(seed);
            Run(w);
            foreach (var d in w.History.Events.Where(e => e.Kind == HistoryEventKind.Desecration))
            {
                desecrations++;
                Assert.Contains(w.History.HolySites, s => s.TileIndex == d.TileIndex);
                Assert.Contains(w.History.Events, e => e.Kind == HistoryEventKind.War && e.Turn == d.Turn && e.TileIndex == d.TileIndex);
            }
        }
        // Desecration needs the battlefield to land on a holy tile; rare in default worlds, so only the shape is asserted when it occurs.
    }
}
