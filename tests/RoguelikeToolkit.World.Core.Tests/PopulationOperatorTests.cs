using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

public class PopulationOperatorTests
{
    private static World Build(int seed) => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory().Build();

    private static void Run(World w, HistoryContext? ctx = null)
        => new HistoryDriver().AddOperator(new PopulationOperator()).Apply(w.Map, w.History, ctx ?? new HistoryContext());

    [Fact]
    public void EveryLivingPolityGetsPopulationAndGenerations()
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            using var w = Build(seed);
            Run(w);
            foreach (var p in w.History.Polities)
            {
                Assert.True(p.Population > 0, $"seed {seed} polity {p.Id}");
                Assert.True(p.RulerGeneration >= 0);
                Assert.InRange(p.Pressure, 0, HistoryDemography.MaxPressure);
            }
        }
    }

    [Fact]
    public void MatchesClosedFormTrajectory_WhenOwnershipIsStatic()
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            using var w = Build(seed);
            var h = w.History;
            var ctx = new HistoryContext();
            double[] cap = HistoryDemography.CarryingCapacities(w.Map.DataStore, h.Polities, h.Ownership);
            Run(w, ctx);
            foreach (var p in h.Polities)
            {
                var folk = HistoryDemography.ResolveFolk(p.FolkKey, null);
                if (!(p.FolkKey is "human" or "dwarf")) continue;
                double rate = HistoryDemography.AnnualToTurnRate(folk.GrowthR, ctx.TurnYears);
                Assert.Equal(HistoryDemography.PopulationAtTurn(cap[p.Id], rate, ctx.TurnCount - 1), p.Population, 6);
                Assert.Equal(HistoryDemography.SuccessionsBefore(folk.LifespanYears, ctx.TurnYears, ctx.TurnCount), p.RulerGeneration);
            }
        }
    }

    [Fact]
    public void SameSeed_SameBytes()
    {
        using var a = Build(5);
        using var b = Build(5);
        Run(a);
        Run(b);
        for (int i = 0; i < a.History.Polities.Count; i++)
        {
            Assert.Equal(a.History.Polities[i].Population, b.History.Polities[i].Population);
            Assert.Equal(a.History.Polities[i].Pressure, b.History.Polities[i].Pressure);
        }
    }

    [Fact]
    public void EndedPolity_IsNotGrown()
    {
        using var w = Build(2);
        var polity = w.History.Polities[0];
        polity.EndedTurn = 0;
        Run(w);
        Assert.Equal(0, polity.Population);
    }
}
