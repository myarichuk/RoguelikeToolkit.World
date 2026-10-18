using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M3.15: plugins define event kinds and operators write the state bag, both round-tripping through save/load.</summary>
public class PluginEventAndStateTests
{
    private sealed class Probe : IHistoryOperator
    {
        public string Name => "probe/op";
        public int Order => 200;
        public string EventKind { get; init; } = "probe/seen";
        public string StateKey { get; init; } = "probe/count";
        public int Seen { get; private set; } = -1;

        public void Propose(TurnView view, ProposalSink sink)
        {
            if (view.Turn != 3) return;
            // Turn 4 would read what turn 3 wrote; here turn 3 writes and records the previous value.
            Seen = view.ReadPluginState(StateKey, 1, out var d) == PluginStateLookup.Found ? d[0] : -1;
            sink.PluginEvent(EventKind, 0, -1, new[] { "t" });
            sink.SetState(StateKey, 1, new byte[] { 7 });
        }
    }

    private static World Build(Probe probe)
        => new WorldBuilder().WithSize(3).WithSeed(5).WithUserEdits().WithHistoryDriver(d => d.AddPluginOperator(probe)).Build();

    private static HistoryCatalog RoundTrip(HistoryCatalog h)
    {
        using var stream = new MemoryStream(h.ToBytes(), writable: false);
        return HistoryCatalog.Load(stream);
    }

    [Fact]
    public void PluginEvent_AndState_RoundTrip()
    {
        using var w = Build(new Probe());
        var loaded = RoundTrip(w.History);
        var e = Assert.Single(loaded.Events, x => x.Kind == HistoryEventKind.Plugin);
        Assert.Equal("probe/seen", e.PluginKind);
        Assert.Equal(PluginStateLookup.Found, loaded.PluginState.TryRead("probe/count", 1, out var data));
        Assert.Equal(new byte[] { 7 }, data);
        Assert.Equal(w.History.ToBytes(), loaded.ToBytes());
    }

    [Fact]
    public void PluginEvent_MustBeInTheOperatorsNamespace()
    {
        Assert.ThrowsAny<Exception>(() => Build(new Probe { EventKind = "other/seen" }).Dispose());
        Assert.ThrowsAny<Exception>(() => Build(new Probe { StateKey = "other/count" }).Dispose());
        Assert.ThrowsAny<Exception>(() => Build(new Probe { EventKind = "bare" }).Dispose());
    }

    [Fact]
    public void BuiltInOperators_CannotUseThePluginChannels()
    {
        var sink = new ProposalSink("Rebellion", 1, 1, new HistoryContext(), new List<Proposal>());
        Assert.Throws<ArgumentException>(() => sink.PluginEvent("probe/x", 0, -1, Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => sink.SetState("probe/x", 1, default));
    }
}
