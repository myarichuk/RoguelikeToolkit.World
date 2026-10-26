using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

public class ConquestOperatorTests
{
    private static World Build(int seed) => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();

    private static void Run(World w)
        => new HistoryDriver()
            .AddOperator(new PopulationOperator())
            .AddOperator(new WarOperator())
            .AddOperator(new ConquestOperator())
            .Apply(w.Map, w.History, new HistoryContext { Features = HistoryFeatures.None }); // the legacy conquest rule; SiegeWorldTests covers it with battles

    [Fact]
    public void Conquests_AnswerAWarOfThePreviousTurn_AndOnlyOverlayCultures()
    {
        int conquests = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            using var w = Build(seed);
            using var twin = Build(seed);
            Run(w);
            Run(twin);
            var h = w.History;
            Assert.Equal(h.Events.Count(e => e.Kind == HistoryEventKind.Conquest),
                twin.History.Events.Count(e => e.Kind == HistoryEventKind.Conquest));
            foreach (var c in h.Events.Where(e => e.Kind == HistoryEventKind.Conquest))
            {
                conquests++;
                int loser = int.Parse(c.Tags.First(t => t.StartsWith("loser:")).Substring(6));
                var war = h.Events.Single(e => e.Id == c.Cause);
                Assert.Equal(HistoryEventKind.War, war.Kind);
                Assert.Equal(c.Turn - 1, war.Turn);
                Assert.Equal(c.PolityId, war.PolityId);
                Assert.Contains($"defender:{loser}", war.Tags);
                Assert.False(string.IsNullOrEmpty(h.Polities[loser].CultureOverlay));
                Assert.Equal(0, h.GrudgeWeight(c.PolityId, loser));
                Assert.Equal(h.Polities[loser].CapitalTile, c.TileIndex);
            }
        }
        Assert.True(conquests > 0, "No conquest across seeds 1-12.");
    }
}
