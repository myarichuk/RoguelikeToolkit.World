using RoguelikeToolkit.World.Core.History;
using Xunit;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M1.2: the turn-major driver, snapshot reads and canonical commit.</summary>
public class HistoryDriverTests
{
    private sealed class Stub : IHistoryOperator
    {
        private readonly Action<TurnView, ProposalSink> _body;
        public Stub(string name, int order, Action<TurnView, ProposalSink> body) { Name = name; Order = order; _body = body; }
        public string Name { get; }
        public int Order { get; }
        public void Propose(TurnView view, ProposalSink sink) => _body(view, sink);
    }

    private static (HistoryCatalog, HistoryContext) World(int polities = 3, int turns = 4)
    {
        var history = new HistoryCatalog(7) { Ownership = new int[16] };
        for (int i = 0; i < polities; i++) history.Polities.Add(new Polity { Id = i, CapitalTile = i });
        history.PolitiesFormed = true;
        return (history, new HistoryContext { TurnCount = turns });
    }

    private static string Log(HistoryCatalog h)
        => string.Join("|", h.Events.Select(e => $"{e.Turn}:{e.PolityId}:{e.Kind}:{string.Join(",", e.Tags)}"));

    [Fact]
    public void TrivialOperator_RunsEveryTurn_AndCommits()
    {
        var (history, ctx) = World();
        var driver = new HistoryDriver().AddOperator(new Stub("Tick", 30,
            (v, s) => s.Event(HistoryEventKind.Exchange, 0, -1, new[] { $"t{v.Turn}" })));
        driver.Apply(null!, history, ctx);
        Assert.Equal(new[] { "t1", "t2", "t3" }, history.Events.Select(e => e.Tags[0]).ToArray());
        Assert.Equal(3, history.SimulatedTurns);
    }

    [Fact]
    public void OperatorsDoNotSeeSameTurnOutput()
    {
        var (history, ctx) = World();
        var seen = new List<int>();
        var driver = new HistoryDriver()
            .AddOperator(new Stub("A", 30, (v, s) => s.Adjust(0, ScalarRegistry.Pressure, 0.5)))
            .AddOperator(new Stub("B", 31, (v, s) => seen.Add((int)(v.ScalarOf(0, ScalarRegistry.Pressure) * 10))));
        driver.Apply(null!, history, ctx);
        Assert.Equal(new[] { 0, 5, 10 }, seen);
    }

    [Fact]
    public void RegistrationAndOrderDoNotChangeBytes()
    {
        string Run(bool reversed, int orderA, int orderB)
        {
            var (history, ctx) = World();
            var a = new Stub("A", orderA, (v, s) =>
            {
                s.Event(HistoryEventKind.War, 1, -1, new[] { "a" });
                s.Adjust(1, ScalarRegistry.Pressure, 0.3);
                s.Event(HistoryEventKind.War, 0, -1, new[] { "a0" });
            });
            var b = new Stub("B", orderB, (v, s) =>
            {
                s.Event(HistoryEventKind.War, 1, -1, new[] { "b" });
                s.Event(HistoryEventKind.Treaty, 0, -1, new[] { "b0" });
                s.Adjust(1, ScalarRegistry.Pressure, 0.4);
            });
            var driver = new HistoryDriver();
            if (reversed) driver.AddOperator(b).AddOperator(a); else driver.AddOperator(a).AddOperator(b);
            driver.Apply(null!, history, ctx);
            return Log(history) + "#" + history.Polities[1].Pressure.ToString("R");
        }
        Assert.Equal(Run(false, 30, 31), Run(true, 30, 31));
        Assert.Equal(Run(false, 30, 31), Run(true, 31, 30));
    }

    [Fact]
    public void CommitOrder_IsEntityThenKindThenOperator()
    {
        var (history, ctx) = World(turns: 2);
        var driver = new HistoryDriver().AddOperator(new Stub("Z", 30, (v, s) =>
        {
            s.Event(HistoryEventKind.War, 2, -1, new[] { "z-e2" });
            s.Event(HistoryEventKind.War, 0, -1, new[] { "z-e0" });
        })).AddOperator(new Stub("A", 30, (v, s) => s.Event(HistoryEventKind.War, 0, -1, new[] { "a-e0" })));
        driver.Apply(null!, history, ctx);
        Assert.Equal(new[] { "a-e0", "z-e0", "z-e2" }, history.Events.Select(e => e.Tags[0]).ToArray());
    }

    [Fact]
    public void EventCap_IsGlobalAcrossOperators()
    {
        var (history, ctx) = World(turns: 2);
        history.SyncBudgets(3, 100);
        var driver = new HistoryDriver()
            .AddOperator(new Stub("A", 30, (v, s) => { for (int i = 0; i < 5; i++) s.Event(HistoryEventKind.War, 2, -1, new[] { "a" }); }))
            .AddOperator(new Stub("B", 31, (v, s) => { for (int i = 0; i < 5; i++) s.Event(HistoryEventKind.War, 0, -1, new[] { "b" }); }));
        driver.Apply(null!, history, ctx);
        Assert.Equal(3, history.Events.Count);
        Assert.Equal(7, history.DroppedEvents);
        Assert.All(history.Events, e => Assert.Equal("b", e.Tags[0]));
    }

    [Fact]
    public void ScalarDeltas_SumThenClampOnce()
    {
        var (history, ctx) = World(turns: 2);
        var driver = new HistoryDriver().AddOperator(new Stub("A", 30, (v, s) =>
        {
            s.Adjust(0, ScalarRegistry.Pressure, 5.0);
            s.Adjust(0, ScalarRegistry.Pressure, -4.5);
        }));
        driver.Apply(null!, history, ctx);
        Assert.Equal(0.5, history.Polities[0].Pressure, 9);
    }

    [Fact]
    public void ConflictingStructuralProposals_AreRejected_NotThrown()
    {
        var (history, ctx) = World(turns: 2);
        var driver = new HistoryDriver()
            .AddOperator(new Stub("A", 30, (v, s) => s.Emit(new Proposal.ReassignTile { EntityId = 1, Tile = 5 })))
            .AddOperator(new Stub("B", 31, (v, s) => s.Emit(new Proposal.ReassignTile { EntityId = 2, Tile = 5 })));
        driver.Apply(null!, history, ctx);
        Assert.Equal(1, history.Ownership[5]);
        Assert.Equal(1, driver.RejectedProposals);
    }

    [Fact]
    public void Rng_IsStablePerOperatorTurnEntityPurpose()
    {
        var (history, ctx) = World(turns: 3);
        var draws = new List<double>();
        var driver = new HistoryDriver().AddOperator(new Stub("R", 30, (v, s) => draws.Add(s.Rng(1, "p").NextDouble())));
        driver.Apply(null!, history, ctx);
        var (h2, c2) = World(turns: 3);
        var again = new List<double>();
        new HistoryDriver().AddOperator(new Stub("R", 30, (v, s) => again.Add(s.Rng(1, "p").NextDouble()))).Apply(null!, h2, c2);
        Assert.Equal(draws, again);
        Assert.NotEqual(draws[0], draws[1]);
    }

    [Fact]
    public void DuplicateOperatorName_Throws()
    {
        var driver = new HistoryDriver().AddOperator(new Stub("A", 1, (v, s) => { }));
        Assert.Throws<InvalidOperationException>(() => driver.AddOperator(new Stub("A", 2, (v, s) => { })));
    }

    [Fact]
    public void PostPass_RunsAfterLastTurn()
    {
        var (history, ctx) = World();
        int seenEvents = -1;
        var driver = new HistoryDriver()
            .AddOperator(new Stub("Tick", 30, (v, s) => s.Event(HistoryEventKind.Exchange, 0, -1, new[] { "x" })))
            .AddPostPass(new PostProbe(h => seenEvents = h.Events.Count));
        driver.Apply(null!, history, ctx);
        Assert.Equal(3, seenEvents);
    }

    private sealed class PostProbe : IHistoryPostPass
    {
        private readonly Action<HistoryCatalog> _run;
        public PostProbe(Action<HistoryCatalog> run) { _run = run; }
        public string Name => "Probe";
        public void Run(HistoryCatalog history, HistoryContext context) => _run(history);
    }
}
