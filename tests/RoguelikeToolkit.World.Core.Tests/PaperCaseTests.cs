using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// M2 exit: the design doc §13 paper cases that M2 covers. A scripted catastrophe on a bare world,
/// then the real <see cref="SettleOperator"/> answers it.
/// </summary>
public class PaperCaseTests
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

    private static World Bare(int seed)
    {
        var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();
        foreach (var s in w.History.Settlements) s.Population = 2000;
        foreach (var p in w.History.Polities) p.Population = w.History.Settlements.Where(s => !s.Ended && s.PolityId == p.Id).Sum(s => s.Population);
        return w;
    }

    private static double Total(HistoryCatalog h)
        => h.Settlements.Where(s => !s.Ended).Sum(s => s.Population) + h.Displaced.Sum(g => g.Amount) + h.PopulationLost;

    private static void Turn(World w, HistoryContext ctx, int turn, params IHistoryOperator[] ops)
    {
        var driver = new HistoryDriver();
        foreach (var op in ops) driver.AddOperator(op);
        driver.RunTurn(w.Map, w.History, ctx, turn);
    }

    /// <summary>Ends the settlement and leaves a ruin where it stood; the survivors become a displaced pool.</summary>
    private static IEnumerable<Proposal> Fall(HistorySettlement s, int settlement, double lostShare, string cause)
    {
        yield return new Proposal.AddScar { EntityId = settlement, Tile = s.TileIndex, Position = s.Position, ScarKind = HistoryScar.Ruin, Cause = cause };
        yield return new Proposal.EndSettlement { EntityId = settlement, Settlement = settlement, EndCause = cause, LostShare = lostShare };
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Gurubashi_CollapsedCapital_LeavesARuinAndRefugeTowns(int seed)
    {
        using var w = Bare(seed);
        var h = w.History;
        var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
        double before = Total(h);
        var capital = h.Settlements.First(s => s.Role == SettlementRole.Capital);
        int index = h.Settlements.IndexOf(capital);

        Turn(w, ctx, 1, new Script(v => Fall(capital, index, 0.2, "razed")));
        Assert.True(capital.Ended);
        Assert.NotEmpty(h.Displaced);
        Assert.Contains(h.Scars, sc => sc.Kind == HistoryScar.Ruin && sc.TileIndex == capital.TileIndex);

        Turn(w, ctx, 2, new SettleOperator());
        var refuges = h.Settlements.Where(s => s.Role == SettlementRole.Refuge && !s.Ended).ToList();
        Assert.NotEmpty(refuges);
        Assert.All(refuges, r => Assert.NotEqual(capital.TileIndex, r.TileIndex));
        Assert.Contains(h.Events, e => e.Kind == HistoryEventKind.SettlementEnded);
        Assert.Contains(h.Events, e => e.Kind == HistoryEventKind.SettlementFounded);
        Assert.Equal(before, Total(h), 6);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Venice_SackedCity_FoundsARefugeElsewhere(int seed)
    {
        using var w = Bare(seed);
        var h = w.History;
        var ctx = new HistoryContext { DisplacedDecayTurns = 50 };
        double before = Total(h);
        const int index = 0;
        var city = h.Settlements[index];
        int polity = city.PolityId;

        Turn(w, ctx, 1, new Script(v => new Proposal[]
        {
            new Proposal.ReleasePopulation { EntityId = index, Settlement = index, Share = 0.8 },
            new Proposal.AddScar { EntityId = index, Tile = city.TileIndex, Position = city.Position, ScarKind = HistoryScar.Ruin, Cause = "sack" },
        }));
        Turn(w, ctx, 2, new SettleOperator());

        var refuge = h.Settlements.Last();
        Assert.Equal(SettlementRole.Refuge, refuge.Role);
        Assert.Equal(polity, refuge.PolityId);
        Assert.NotEqual(city.TileIndex, refuge.TileIndex);
        Assert.Empty(h.Displaced);
        Assert.False(city.Ended);
        Assert.Equal(before, Total(h), 6);
    }
}
