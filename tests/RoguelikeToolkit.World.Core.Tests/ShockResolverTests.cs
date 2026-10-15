using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.5A: shocks strike settlements by intensity, kill and displace people, and keep the ledger balanced.</summary>
public class ShockResolverTests
{
    private sealed class ShockOp : IHistoryOperator
    {
        private readonly Func<Shock> _make;
        public int Struck;
        public ShockOp(Func<Shock> make) => _make = make;
        public string Name => "ShockOp";
        public int Order => 1000;
        public void Propose(TurnView view, ProposalSink sink) => Struck = ShockResolver.Resolve(view, sink, _make());
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

    private static void Turn(World w, HistoryContext ctx, int turn, IHistoryOperator op)
        => new HistoryDriver().AddOperator(op).RunTurn(w.Map, w.History, ctx, turn);

    [Fact]
    public void Hamlet_HitAtFullSeverity_IsAbandoned_AndLedgerBalances()
    {
        using var w = Bare();
        Seed(w);
        var site = w.History.Settlements[0];
        site.Tier = SettlementTier.Hamlet;
        site.Role = SettlementRole.Colony;
        double before = Total(w.History);
        var op = new ShockOp(() => new Shock { Kind = "flood", Severity = 1, Tile = site.TileIndex, Casualty = 0.25 });
        Turn(w, new HistoryContext { DisplacedDecayTurns = 50 }, 1, op);
        Assert.Equal(1, op.Struck);
        Assert.True(site.Ended);
        Assert.Equal("flood", site.EndCause);
        Assert.Equal(25, w.History.PopulationLost, 6);
        Assert.Equal(75, w.History.Displaced.Sum(g => g.Amount), 6);
        Assert.Equal(before, Total(w.History), 6);
        Assert.Contains(w.History.Events, e => e.Kind == HistoryEventKind.Shock && e.Tags.Contains("shock:flood") && e.Tags.Contains("struck:1"));
    }

    [Fact]
    public void CapitalCity_IsHurtNotAbandoned_AndConserves()
    {
        using var w = Bare();
        Seed(w);
        var site = w.History.Settlements[0];
        var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
        var op = new ShockOp(() => new Shock { Kind = "quake", Severity = 1, Tile = site.TileIndex });
        int hits = 0;
        for (int turn = 1; turn <= 20; turn++)
        {
            site.Population = 100;
            double before = Total(w.History);
            Turn(w, ctx, turn, op);
            hits += op.Struck;
            Assert.False(site.Ended);
            Assert.Equal(before, Total(w.History), 6);
            w.History.Displaced.Clear();
        }
        Assert.InRange(hits, 1, 19);
    }

    [Fact]
    public void ShockElsewhere_StrikesNothing()
    {
        using var w = Bare();
        Seed(w);
        int away = Enumerable.Range(0, w.Map.DataStore.GetTileVectors().Length).First(t => w.History.Settlements.All(s => s.TileIndex != t));
        var op = new ShockOp(() => new Shock { Kind = "quake", Severity = 1, Tile = away });
        Turn(w, new HistoryContext(), 1, op);
        Assert.Equal(0, op.Struck);
        Assert.DoesNotContain(w.History.Events, e => e.Kind == HistoryEventKind.Shock);
    }

    [Fact]
    public void PolityShock_ReachesOnlyThatPolity()
    {
        using var w = Bare();
        Seed(w);
        foreach (var s in w.History.Settlements) { s.Tier = SettlementTier.Hamlet; s.Role = SettlementRole.Colony; }
        var op = new ShockOp(() => new Shock { Kind = "famine", Severity = 1, Footprint = ShockFootprint.Polity, PolityId = 0 });
        Turn(w, new HistoryContext { DisplacedDecayTurns = 50 }, 1, op);
        foreach (var s in w.History.Settlements)
            Assert.Equal(s.PolityId == 0, s.Ended);
        Assert.Equal(w.History.Settlements.Count(s => s.PolityId == 0), op.Struck);
    }

    [Fact]
    public void Shocks_AreDeterministic()
    {
        byte[] Run()
        {
            using var w = Bare();
            Seed(w);
            var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
            var op = new ShockOp(() => new Shock { Kind = "plague", Severity = 0.9, Footprint = ShockFootprint.Global, Casualty = 0.5 });
            for (int turn = 1; turn <= 5; turn++) Turn(w, ctx, turn, op);
            return w.History.ToBytes();
        }
        Assert.Equal(Run(), Run());
    }
}
