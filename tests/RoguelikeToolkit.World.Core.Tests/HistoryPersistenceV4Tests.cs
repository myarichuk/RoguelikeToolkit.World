using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M1.4: schema v4 round-trips the generic-kernel state and rejects corrupt references.</summary>
public class HistoryPersistenceV4Tests
{
    private static World Build(int seed = 42)
        => new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits().WithHistory().Build();

    private static HistoryCatalog RoundTrip(HistoryCatalog h)
    {
        using var stream = new MemoryStream(h.ToBytes(), writable: false);
        return HistoryCatalog.Load(stream);
    }

    private static HistoryCatalog Rich()
    {
        using var w = Build();
        var h = w.History;
        int role = h.Strings.Intern("attacker");
        int cause = h.Strings.Intern("collapse");
        h.Scalars.Declare("unrest", 0, 10, 2);
        h.Polities[0].SetScalar(1, 7.5, h.Scalars);
        h.Polities[1].EndedTurn = 12;
        h.Polities[1].EndCause = cause;
        h.Polities[1].Parent = 0;
        h.AppendLinked(HistoryEventKind.War, 3, 0, -1, new[] { "a" }, -1, new[] { new EventParticipant(0, role), new EventParticipant(1, role) });
        int first = h.Events[^1].Id;
        h.AppendLinked(HistoryEventKind.Conquest, 4, 0, -1, new[] { "b" }, first, null);
        h.AddGrudge(0, 1, 2, turn: 5);
        h.AddGrudge(0, 1, 1, turn: 9);
        return h;
    }

    [Fact]
    public void V4_RoundTripsKernelState_ByteForByte()
    {
        var h = Rich();
        var loaded = RoundTrip(h);
        Assert.Equal(h.ToBytes(), loaded.ToBytes());
        Assert.Equal(13, HistoryCatalog.SchemaVersion);
        Assert.Equal(h.Strings.Count, loaded.Strings.Count);
        Assert.Equal(7.5, loaded.Polities[0].GetScalar(1, loaded.Scalars));
        Assert.Equal(12, loaded.Polities[1].EndedTurn);
        Assert.False(loaded.Polities[1].Alive);
        Assert.Equal(0, loaded.Polities[1].Parent);
        Assert.Equal("collapse", loaded.Strings.NameOf(loaded.Polities[1].EndCause));
        var linked = loaded.Events.Single(e => e.Kind == HistoryEventKind.Conquest && e.Tags.Contains("b"));
        Assert.Equal(h.Events.Single(e => e.Tags.Contains("b")).Cause, linked.Cause);
        Assert.Equal(2, loaded.Events.Single(e => e.Tags.Contains("a")).Participants.Count);
        var grudge = loaded.Grudges.Single(g => g.A == 0 && g.B == 1);
        Assert.Equal(5, grudge.FirstTurn);
        Assert.Equal(9, grudge.LastTurn);
    }

    [Fact]
    public void V4_KeepsEventIdsStable_AcrossCompaction()
    {
        var h = Rich();
        int next = h.Events.Max(e => e.Id) + 1;
        var loaded = RoundTrip(h);
        loaded.AppendLinked(HistoryEventKind.Myth, 20, 0, -1, Array.Empty<string>(), -1, null);
        Assert.Equal(next, loaded.Events[^1].Id);
    }

    [Fact]
    public void V4_OverridesAreCarried()
    {
        var h = Rich();
        h.Overrides.RenamePolity(0, "Aldor");
        h.Overrides.PinTile(3, 1);
        var loaded = RoundTrip(h);
        Assert.Equal("Aldor", loaded.DisplayNameOf(0));
        Assert.Equal(1, loaded.Overrides.PinnedTiles[3]);
    }

    [Fact]
    public void V4_RejectsCorruptReferences()
    {
        var h = Rich();
        h.Polities[1].Parent = 99;
        Assert.Throws<InvalidDataException>(() => RoundTrip(h));

        h = Rich();
        h.Polities[1].EndCause = 999;
        Assert.Throws<InvalidDataException>(() => RoundTrip(h));

        h = Rich();
        h.AppendLinked(HistoryEventKind.War, 6, 0, -1, Array.Empty<string>(), -1, new[] { new EventParticipant(0, 999) });
        Assert.Throws<InvalidDataException>(() => RoundTrip(h));

        h = Rich();
        h.AppendLinked(HistoryEventKind.War, 6, 0, -1, Array.Empty<string>(), 5000, null);
        Assert.Throws<InvalidDataException>(() => RoundTrip(h));
    }

    [Fact]
    public void V4_RejectsTruncation()
    {
        byte[] bytes = Rich().ToBytes();
        foreach (int cut in new[] { bytes.Length - 1, bytes.Length / 2, 30 })
        {
            using var stream = new MemoryStream(bytes, 0, cut, writable: false);
            Assert.Throws<InvalidDataException>(() => HistoryCatalog.Load(stream));
        }
    }
}
