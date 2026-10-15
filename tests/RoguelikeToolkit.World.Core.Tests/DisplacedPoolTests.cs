using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.4B: the displaced pool conserves people between releases, absorptions and ended settlements.</summary>
public class DisplacedPoolTests
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

    private static void Run(World w, HistoryContext ctx, int turn, Func<TurnView, IEnumerable<Proposal>> emit)
        => new HistoryDriver().AddOperator(new Script(emit)).RunTurn(w.Map, w.History, ctx, turn);

    private static double Total(HistoryCatalog h)
        => h.Settlements.Where(s => !s.Ended).Sum(s => s.Population) + h.Displaced.Sum(g => g.Amount) + h.PopulationLost;

    private static void Seed(World w)
    {
        foreach (var s in w.History.Settlements) s.Population = 100;
        foreach (var p in w.History.Polities) p.Population = 100;
    }

    [Fact]
    public void Release_MovesPeopleIntoThePool_AndConserves()
    {
        using var w = Bare();
        Seed(w);
        double before = Total(w.History);
        Run(w, new HistoryContext(), 1, _ => new Proposal[] { new Proposal.ReleasePopulation { EntityId = 0, Settlement = 0, Share = 0.25, Cause = -1 } });
        var group = Assert.Single(w.History.Displaced);
        Assert.Equal((0, 25.0, 0), (group.PolityId, group.Amount, group.Origin));
        Assert.Equal(75, w.History.Settlements[0].Population);
        Assert.Equal(75, w.History.Polities[0].Population);
        Assert.Equal(before, Total(w.History), 6);
    }

    [Fact]
    public void Absorb_ClampsToGroup_AndRefusesOtherPolities()
    {
        using var w = Bare();
        Seed(w);
        var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
        double before = Total(w.History);
        Run(w, ctx, 1, _ => new Proposal[] { new Proposal.ReleasePopulation { EntityId = 0, Settlement = 0, Share = 0.5 } });
        Run(w, ctx, 2, v => new Proposal[]
        {
            new Proposal.AbsorbDisplaced { EntityId = 0, Group = 0, Settlement = 0, Amount = 30 },
            new Proposal.AbsorbDisplaced { EntityId = 1, Group = 0, Settlement = 1, Amount = 10 },
            new Proposal.AbsorbDisplaced { EntityId = 0, Group = 0, Settlement = 0, Amount = 1000 },
        });
        Assert.Empty(w.History.Displaced);
        Assert.Equal(100, w.History.Settlements[0].Population, 6);
        Assert.Equal(before, Total(w.History), 6);
    }

    [Fact]
    public void Asylum_LetsAHomelessPolityPlaceItsPeopleElsewhere_AndConserves()
    {
        using var w = Bare();
        Seed(w);
        var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
        double before = Total(w.History);
        Run(w, ctx, 1, _ => new Proposal[] { new Proposal.EndSettlement { EntityId = 0, Settlement = 0, EndCause = "razed" } });
        Assert.Equal(100, w.History.Displaced.Sum(g => g.Amount), 6);
        Run(w, ctx, 2, _ => new Proposal[] { new Proposal.AbsorbDisplaced { EntityId = 0, Group = 0, Settlement = 1, Amount = 1000 } });
        Assert.Empty(w.History.Displaced);
        Assert.Equal(200, w.History.Settlements[1].Population, 6);
        Assert.Equal(before, Total(w.History), 6);
    }

    [Fact]
    public void EndSettlement_ReleasesItsPeople_AndStaleGroupsReturnHome()
    {
        using var w = Bare();
        Seed(w);
        var ctx = new HistoryContext { DisplacedDecayTurns = 3 };
        int colony = w.History.Settlements.Count;
        int tile = Enumerable.Range(0, w.History.Ownership.Length).First(t => w.History.Ownership[t] == 0 && t != w.History.Polities[0].CapitalTile);
        Run(w, ctx, 1, _ => new Proposal[] { new Proposal.AddSettlement { EntityId = 0, Tile = tile, Parent = 0 } });
        double before = Total(w.History);
        Run(w, ctx, 2, _ => new Proposal[] { new Proposal.EndSettlement { EntityId = 0, Settlement = colony, EndCause = "razed" } });
        var group = Assert.Single(w.History.Displaced);
        Assert.Equal(colony, group.Origin);
        Assert.Equal(0, w.History.Settlements[colony].Population);
        Assert.True(group.Cause >= 0, "The group should point at the SettlementEnded event.");
        Assert.Equal(before, Total(w.History), 6);
        double home = w.History.Settlements[0].Population;
        double displaced = group.Amount;
        Run(w, ctx, 3, _ => Array.Empty<Proposal>());
        Run(w, ctx, 4, _ => Array.Empty<Proposal>());
        Assert.Single(w.History.Displaced);
        Run(w, ctx, 5, _ => Array.Empty<Proposal>());
        Assert.Empty(w.History.Displaced);
        Assert.Equal(home + displaced, w.History.Settlements[0].Population, 6);
        Assert.Equal(w.History.Settlements[0].Population + w.History.Settlements.Where(s => !s.Ended && s.PolityId == 0).Skip(1).Sum(s => s.Population), w.History.Polities[0].Population, 6);
        Assert.Equal(0, w.History.PopulationLost);
        Assert.Equal(before, Total(w.History), 6);
    }

    [Fact]
    public void StaleGroupOfADeadPolity_IsRecordedAsLost()
    {
        using var w = Bare();
        Seed(w);
        var ctx = new HistoryContext { DisplacedDecayTurns = 1 };
        var mine = w.History.Settlements.Select((s, i) => (s, i)).Where(x => x.s.PolityId == 0).Select(x => x.i).ToList();
        Run(w, ctx, 1, _ => mine.Select(i => (Proposal)new Proposal.EndSettlement { EntityId = 0, Settlement = i, EndCause = "razed" }));
        Assert.Equal(0, w.History.Polities[0].Population);
        Assert.Equal(100.0 * mine.Count, w.History.Displaced.Sum(g => g.Amount), 6);
        Run(w, ctx, 2, _ => Array.Empty<Proposal>());
        Assert.Empty(w.History.Displaced);
        Assert.Equal(100.0 * mine.Count, w.History.PopulationLost, 6);
    }

    [Fact]
    public void PoolRoundTrips_AndIsDeterministic()
    {
        using var a = Bare();
        Seed(a);
        var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
        Run(a, ctx, 1, _ => new Proposal[] { new Proposal.ReleasePopulation { EntityId = 0, Settlement = 0, Share = 0.4, Cause = -1 } });
        byte[] bytes = a.History.ToBytes();
        HistoryCatalog loaded;
        using (var stream = new MemoryStream(bytes, writable: false)) loaded = HistoryCatalog.Load(stream);
        Assert.Equal(bytes, loaded.ToBytes());
        var g = Assert.Single(loaded.Displaced);
        Assert.Equal(40, g.Amount, 6);
    }
}
