using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M3.3: the tier ladder follows settlement fill, with a hysteresis band and a capital floor.</summary>
public class TierOperatorTests
{
    private static World Bare()
        => new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();

    private sealed class Script : IHistoryOperator
    {
        private readonly Func<TurnView, IEnumerable<Proposal>> _emit;
        public Script(Func<TurnView, IEnumerable<Proposal>> emit) => _emit = emit;
        public string Name => "Script";
        public int Order => 1000;
        public void Propose(TurnView view, ProposalSink sink) { foreach (var p in _emit(view)) sink.Emit(p); }
    }

    private static void RunTier(World w, int turn = 30)
        => new HistoryDriver().AddOperator(new TierOperator()).RunTurn(w.Map, w.History, new HistoryContext(), turn);

    /// <summary>Sets settlement 0 to a non-capital with the given tier and a population at <paramref name="fill"/> of its capacity.</summary>
    private static void Prime(World w, SettlementTier tier, double fill)
    {
        var s = w.History.Settlements[0];
        s.Role = SettlementRole.Colony;
        s.Tier = tier;
        s.Population = 0;
        var view = new TurnView(w.Map, w.History, new HistoryContext(), 30);
        s.Population = fill * view.SettlementCapacity(0);
    }

    [Fact]
    public void Hysteresis_HoldsInsideTheBand_AndStepsOutsideIt()
    {
        using var w = Bare();
        Prime(w, SettlementTier.City, 0.6);
        RunTier(w);
        Assert.Equal(SettlementTier.City, w.History.Settlements[0].Tier);

        Prime(w, SettlementTier.City, 0.4);
        RunTier(w);
        Assert.Equal(SettlementTier.Town, w.History.Settlements[0].Tier);

        Prime(w, SettlementTier.Town, 0.6);
        RunTier(w);
        Assert.Equal(SettlementTier.Town, w.History.Settlements[0].Tier); // 0.6 held a City, but does not make one

        Prime(w, SettlementTier.Town, 0.9);
        RunTier(w);
        Assert.Equal(SettlementTier.City, w.History.Settlements[0].Tier);
        var changes = w.History.Events.Where(e => e.Kind == HistoryEventKind.SettlementTierChanged && e.Tags.Contains("settlement:0")).ToList();
        Assert.Equal(2, changes.Count);
        Assert.Contains("down", changes[0].Tags);
        Assert.Contains("from:City", changes[0].Tags);
        Assert.Contains("to:Town", changes[0].Tags);
    }

    [Fact]
    public void YoungSettlements_AreLeftAlone_AndCapitalsKeepTheirFloor()
    {
        using var w = Bare();
        Prime(w, SettlementTier.City, 0.01);
        RunTier(w, turn: TierOperator.GraceTurns - 1);
        Assert.Equal(SettlementTier.City, w.History.Settlements[0].Tier);

        w.History.Settlements[0].Role = SettlementRole.Capital;
        w.History.Settlements[0].Tier = SettlementTier.Town;
        RunTier(w);
        Assert.Equal(SettlementTier.Town, w.History.Settlements[0].Tier);
    }

    [Fact]
    public void ChangeTier_Commit_RefusesJumps_ForeignOwners_AndCapitalsBelowFloor()
    {
        using var w = Bare();
        var s = w.History.Settlements[0];
        s.Role = SettlementRole.Colony;
        s.Tier = SettlementTier.Village;
        var driver = new HistoryDriver().AddOperator(new Script(_ => new Proposal[]
        {
            new Proposal.ChangeTier { EntityId = s.PolityId, Settlement = 0, Tier = SettlementTier.City },     // two steps
        }));
        driver.RunTurn(w.Map, w.History, new HistoryContext(), 1);
        Assert.Equal(SettlementTier.Village, s.Tier);
        Assert.Equal(1, driver.RejectedProposals);

        driver = new HistoryDriver().AddOperator(new Script(_ => new Proposal[]
        {
            new Proposal.ChangeTier { EntityId = s.PolityId + 1, Settlement = 0, Tier = SettlementTier.Town }, // not the owner
        }));
        driver.RunTurn(w.Map, w.History, new HistoryContext(), 2);
        Assert.Equal(SettlementTier.Village, s.Tier);

        s.Role = SettlementRole.Capital;
        s.Tier = SettlementTier.Town;
        driver = new HistoryDriver().AddOperator(new Script(_ => new Proposal[]
        {
            new Proposal.ChangeTier { EntityId = s.PolityId, Settlement = 0, Tier = SettlementTier.Village },
        }));
        driver.RunTurn(w.Map, w.History, new HistoryContext(), 3);
        Assert.Equal(SettlementTier.Town, s.Tier);
        Assert.DoesNotContain(w.History.Events, e => e.Kind == HistoryEventKind.SettlementTierChanged);
    }

    [Fact]
    public void FullRuns_TierEventsChainOneStepAtATime_AndEndOnTheStoredTier()
    {
        int total = 0;
        for (int seed = 1; seed <= 6; seed++)
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().Build();
            var h = w.History;
            foreach (var g in h.Events.Where(e => e.Kind == HistoryEventKind.SettlementTierChanged)
                         .GroupBy(e => e.Tags.First(t => t.StartsWith("settlement:", StringComparison.Ordinal))))
            {
                SettlementTier? prev = null;
                SettlementTier last = default;
                foreach (var e in g)
                {
                    var from = Enum.Parse<SettlementTier>(e.Tags.First(t => t.StartsWith("from:", StringComparison.Ordinal))[5..]);
                    var to = Enum.Parse<SettlementTier>(e.Tags.First(t => t.StartsWith("to:", StringComparison.Ordinal))[3..]);
                    Assert.Equal(1, Math.Abs((int)to - (int)from));
                    if (prev != null) Assert.Equal(prev, from);
                    prev = to; last = to; total++;
                }
                int index = int.Parse(g.Key["settlement:".Length..]);
                Assert.Equal(last, h.Settlements[index].Tier);
            }
        }
        Assert.True(total > 0, "Expected some settlement to change tier across six worlds.");
    }
}
