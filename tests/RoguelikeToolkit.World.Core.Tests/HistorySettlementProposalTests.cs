using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.3B: the AddSettlement / EndSettlement proposals and their commit rules.</summary>
public class HistorySettlementProposalTests
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

    private static int OwnedNonCapitalTile(World w, int polity)
    {
        for (int t = 0; t < w.History.Ownership.Length; t++)
            if (w.History.Ownership[t] == polity && t != w.History.Polities[polity].CapitalTile) return t;
        throw new InvalidOperationException("Polity owns only its capital.");
    }

    private static HistoryDriver Run(World w, HistoryContext ctx, Func<TurnView, IEnumerable<Proposal>> emit, int turn = 1)
    {
        var driver = new HistoryDriver().AddOperator(new Script(emit));
        driver.RunTurn(w.Map, w.History, ctx, turn);
        return driver;
    }

    [Fact]
    public void AddSettlement_FoundsAndLogsOnce()
    {
        using var w = Bare();
        int tile = OwnedNonCapitalTile(w, 0);
        int before = w.History.Settlements.Count;
        Run(w, new HistoryContext(), _ => new Proposal[]
        {
            new Proposal.AddSettlement { EntityId = 0, Tile = tile, Parent = 0, Role = SettlementRole.Colony },
        });
        Assert.Equal(before + 1, w.History.Settlements.Count);
        var s = w.History.Settlements[^1];
        Assert.Equal((tile, 0, 1, SettlementRole.Colony, SettlementTier.Hamlet, 0), (s.TileIndex, s.PolityId, s.FoundedTurn, s.Role, s.Tier, s.Parent));
        Assert.False(s.Ended);
        var ev = Assert.Single(w.History.Events, e => e.Kind == HistoryEventKind.SettlementFounded);
        Assert.Equal(tile, ev.TileIndex);
        Assert.Contains($"settlement:{before}", ev.Tags);
        Assert.Contains("from:0", ev.Tags);
    }

    [Fact]
    public void AddSettlement_RefusesForeignTile_DuplicateTile_AndCap()
    {
        using var w = Bare();
        int tile = OwnedNonCapitalTile(w, 0);
        int foreign = OwnedNonCapitalTile(w, 1);
        int before = w.History.Settlements.Count;
        var driver = Run(w, new HistoryContext(), _ => new Proposal[]
        {
            new Proposal.AddSettlement { EntityId = 0, Tile = foreign },
            new Proposal.AddSettlement { EntityId = 0, Tile = w.History.Polities[0].CapitalTile },
            new Proposal.AddSettlement { EntityId = 0, Tile = tile, Parent = 999 },
        });
        Assert.Equal(before, w.History.Settlements.Count);
        Assert.Equal(3, driver.RejectedProposals);

        using var capped = Bare();
        var ctx = new HistoryContext { MaxSettlementsPerPolity = 1 };
        int cappedBefore = capped.History.Settlements.Count;
        Run(capped, ctx, _ => new Proposal[] { new Proposal.AddSettlement { EntityId = 0, Tile = OwnedNonCapitalTile(capped, 0) } });
        Assert.Equal(cappedBefore, capped.History.Settlements.Count);
    }

    [Fact]
    public void TwoFoundingsOnOneTile_OnlyOneWins()
    {
        using var w = Bare();
        int tile = OwnedNonCapitalTile(w, 0);
        int before = w.History.Settlements.Count;
        var driver = Run(w, new HistoryContext(), _ => new Proposal[]
        {
            new Proposal.AddSettlement { EntityId = 0, Tile = tile },
            new Proposal.AddSettlement { EntityId = 0, Tile = tile },
        });
        Assert.Equal(before + 1, w.History.Settlements.Count);
        Assert.Equal(1, driver.RejectedProposals);
    }

    [Fact]
    public void EndSettlement_EndsOnce_AndFreesTheTileForRefounding()
    {
        using var w = Bare();
        int tile = OwnedNonCapitalTile(w, 0);
        Run(w, new HistoryContext(), _ => new Proposal[] { new Proposal.AddSettlement { EntityId = 0, Tile = tile } });
        int index = w.History.Settlements.Count - 1;
        var driver = Run(w, new HistoryContext(), _ => new Proposal[]
        {
            new Proposal.EndSettlement { EntityId = 0, Settlement = index, EndCause = "razed" },
            new Proposal.EndSettlement { EntityId = 0, Settlement = index, EndCause = "abandoned" },
        }, turn: 2);
        var s = w.History.Settlements[index];
        Assert.True(s.Ended);
        Assert.Equal((2, "razed"), (s.EndedTurn, s.EndCause));
        Assert.Equal(1, driver.RejectedProposals);
        Assert.Single(w.History.Events, e => e.Kind == HistoryEventKind.SettlementEnded);

        Run(w, new HistoryContext(), _ => new Proposal[] { new Proposal.AddSettlement { EntityId = 0, Tile = tile } }, turn: 3);
        Assert.Equal(index + 2, w.History.Settlements.Count);
    }

    [Fact]
    public void Settlements_RoundTripWithEvents()
    {
        using var w = Bare();
        Run(w, new HistoryContext(), _ => new Proposal[] { new Proposal.AddSettlement { EntityId = 0, Tile = OwnedNonCapitalTile(w, 0), Parent = 0 } });
        byte[] bytes = w.History.ToBytes();
        HistoryCatalog loaded;
        using (var stream = new MemoryStream(bytes, writable: false)) loaded = HistoryCatalog.Load(stream);
        Assert.Equal(bytes, loaded.ToBytes());
        Assert.Contains(loaded.Events, e => e.Kind == HistoryEventKind.SettlementFounded);
    }

    [Fact]
    public void TurnView_ExposesSettlementsAtTheSnapshot()
    {
        using var w = Bare();
        var seen = new List<int>();
        Run(w, new HistoryContext(), view =>
        {
            seen.Add(view.Settlements.Count);
            return new Proposal[] { new Proposal.AddSettlement { EntityId = 0, Tile = OwnedNonCapitalTile(w, 0) } };
        });
        Assert.Equal(new[] { w.History.Polities.Count }, seen);
        Assert.Equal(6, new HistoryContext().MaxSettlementsPerPolity);
    }

    [Fact]
    public void ReassignedTile_MovesItsStandingSettlementToTheNewOwner()
    {
        using var w = Bare();
        int tile = OwnedNonCapitalTile(w, 0);
        Run(w, new HistoryContext(), _ => new Proposal[] { new Proposal.AddSettlement { EntityId = 0, Tile = tile } });
        int index = w.History.Settlements.Count - 1;
        Run(w, new HistoryContext(), _ => new Proposal[] { new Proposal.ReassignTile { EntityId = 1, Tile = tile } }, turn: 2);
        Assert.Equal(1, w.History.Settlements[index].PolityId);
    }
}
