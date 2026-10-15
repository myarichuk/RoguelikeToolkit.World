using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M2.5B: per-settlement capacity and pressure are visible to operators and steer refugees.</summary>
public class SettlementPressureTests
{
    private sealed class Probe : IHistoryOperator
    {
        public readonly List<(double Capacity, double Pressure)> Seen = new();
        public string Name => "Probe";
        public int Order => 1000;
        public void Propose(TurnView view, ProposalSink sink)
        {
            Seen.Clear();
            foreach (var s in view.Settlements)
                if (!s.Ended) Seen.Add((view.SettlementCapacity(s.Index), view.SettlementPressure(s.Index)));
        }
    }

    private static World Bare()
        => new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();

    [Fact]
    public void Pressure_IsPeopleOverCapacity()
    {
        using var w = Bare();
        foreach (var s in w.History.Settlements) s.Population = 100;
        foreach (var p in w.History.Polities) p.Population = 100;
        var probe = new Probe();
        new HistoryDriver().AddOperator(probe).RunTurn(w.Map, w.History, new HistoryContext(), 1);
        Assert.NotEmpty(probe.Seen);
        foreach (var (cap, pressure) in probe.Seen)
        {
            Assert.True(cap > 0);
            Assert.Equal(HistoryDemography.PressureOf(100, cap), pressure, 9);
        }
    }

    [Fact]
    public void OutOfRange_IsZero()
    {
        using var w = Bare();
        TurnView? seen = null;
        var op = new CaptureOp(v => seen = v);
        new HistoryDriver().AddOperator(op).RunTurn(w.Map, w.History, new HistoryContext(), 1);
        Assert.Equal(0, seen!.SettlementCapacity(-1));
        Assert.Equal(0, seen.SettlementPressure(9999));
    }

    private sealed class CaptureOp : IHistoryOperator
    {
        private readonly Action<TurnView> _on;
        public CaptureOp(Action<TurnView> on) => _on = on;
        public string Name => "Capture";
        public int Order => 1000;
        public void Propose(TurnView view, ProposalSink sink) => _on(view);
    }
}
