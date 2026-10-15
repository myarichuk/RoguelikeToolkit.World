using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.4C: displaced groups found refuges or merge into a standing settlement of their own polity.</summary>
public class RefugeSettlingTests
{
    private sealed class Script : IHistoryOperator
    {
        private readonly Func<TurnView, IEnumerable<Proposal>> _emit;
        public Script(Func<TurnView, IEnumerable<Proposal>> emit) => _emit = emit;
        public string Name => "Script";
        public int Order => 1000;
        public void Propose(TurnView view, ProposalSink sink)
        {
            foreach (var p in _emit(view)) sink.Emit(p);
        }
    }

    private static World Bare()
        => new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();

    private static double Total(HistoryCatalog h)
        => h.Settlements.Where(s => !s.Ended).Sum(s => s.Population) + h.Displaced.Sum(g => g.Amount) + h.PopulationLost;

    private static void Seed(World w)
    {
        foreach (var s in w.History.Settlements) s.Population = 100;
        foreach (var p in w.History.Polities) p.Population = 100;
    }

    private static void Turn(World w, HistoryContext ctx, int turn, params IHistoryOperator[] ops)
    {
        var driver = new HistoryDriver();
        foreach (var op in ops) driver.AddOperator(op);
        driver.RunTurn(w.Map, w.History, ctx, turn);
    }

    [Fact]
    public void LargeGroup_FoundsARefuge_AndConserves()
    {
        using var w = Bare();
        Seed(w);
        var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
        double before = Total(w.History);
        Turn(w, ctx, 1, new Script(_ => new Proposal[] { new Proposal.ReleasePopulation { EntityId = 0, Settlement = 0, Share = 0.8 } }));
        Assert.Single(w.History.Displaced);
        Turn(w, ctx, 2, new SettleOperator());
        var refuge = w.History.Settlements.Last();
        Assert.Equal(SettlementRole.Refuge, refuge.Role);
        Assert.Equal(0, refuge.PolityId);
        Assert.Equal(80, refuge.Population, 6);
        Assert.Empty(w.History.Displaced);
        Assert.Equal(before, Total(w.History), 6);
    }

    [Fact]
    public void SmallGroup_MergesIntoAStandingSettlement()
    {
        using var w = Bare();
        Seed(w);
        var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
        int count = w.History.Settlements.Count;
        double before = Total(w.History);
        Turn(w, ctx, 1, new Script(_ => new Proposal[] { new Proposal.ReleasePopulation { EntityId = 0, Settlement = 0, Share = 0.1 } }));
        Turn(w, ctx, 2, new SettleOperator());
        Assert.Equal(count, w.History.Settlements.Count);
        Assert.Empty(w.History.Displaced);
        Assert.Equal(100, w.History.Settlements[0].Population, 6);
        Assert.Equal(before, Total(w.History), 6);
    }

    [Fact]
    public void RefugeFounding_IsDeterministic()
    {
        byte[] Run()
        {
            using var w = Bare();
            Seed(w);
            var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
            Turn(w, ctx, 1, new Script(_ => new Proposal[] { new Proposal.ReleasePopulation { EntityId = 0, Settlement = 0, Share = 0.8 } }));
            Turn(w, ctx, 2, new SettleOperator());
            return w.History.ToBytes();
        }
        Assert.Equal(Run(), Run());
    }
}
