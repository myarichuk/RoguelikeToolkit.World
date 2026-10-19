using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M3.20: the per-turn timeline (turn-0 ownership, territory changes, polity samples) an editor scrubs through.</summary>
public class HistoryTimelineTests
{
    private static readonly int[] Seeds = { 42, 7, 1234 };

    private static World Build(int seed, int? turns = null, Action<ScalarRegistry>? scalars = null)
    {
        var b = new WorldBuilder().WithSize(3).WithSeed(seed).WithUserEdits();
        if (turns is int t) b.WithHistory(c => c.TurnCount = t);
        if (scalars != null) b.WithHistoryScalars(scalars);
        return b.Build();
    }

    private static HistoryCatalog RoundTrip(HistoryCatalog h)
    {
        using var stream = new MemoryStream(h.ToBytes(), writable: false);
        return HistoryCatalog.Load(stream);
    }

    [Fact]
    public void Replay_FromTheBase_EndsAtTheFinalOwnership()
    {
        bool anyChange = false;
        foreach (int seed in Seeds)
        {
            using var w = Build(seed);
            var h = w.History;
            Assert.True(h.Timeline.Recorded);
            Assert.Equal(h.Ownership.Length, h.Timeline.BaseOwnership.Length);
            Assert.Equal(h.Timeline.BaseOwnership, h.OwnershipAt(0));
            Assert.Equal(h.Ownership, h.OwnershipAt(h.SimulatedTurns));
            anyChange |= h.Timeline.Changes.Count > 0;
            for (int i = 1; i < h.Timeline.Changes.Count; i++)
            {
                var (a, b) = (h.Timeline.Changes[i - 1], h.Timeline.Changes[i]);
                Assert.True(a.Turn < b.Turn || (a.Turn == b.Turn && a.Tile < b.Tile));
            }
            foreach (var c in h.Timeline.Changes) Assert.InRange(c.Turn, 1, h.SimulatedTurns);
        }
        Assert.True(anyChange, "No seed changed a single tile's owner; the log is untested.");
    }

    [Fact]
    public void EachChange_IsARealChangeOfOwner()
    {
        using var w = Build(42);
        var h = w.History;
        int[] owners = (int[])h.Timeline.BaseOwnership.Clone();
        foreach (var c in h.Timeline.Changes)
        {
            Assert.NotEqual(owners[c.Tile], c.Owner);
            owners[c.Tile] = c.Owner;
        }
    }

    [Fact]
    public void Samples_CoverEveryTurn_ForEveryLivingPolity_WithEveryScalar()
    {
        using var w = Build(7, scalars: r => r.DeclarePlugin("test/morale", 0, 1, 0.25));
        var h = w.History;
        for (int turn = 0; turn <= h.SimulatedTurns; turn++)
        {
            var expected = h.Polities.Where(p => p.FoundedTurn <= turn && (p.Alive || p.EndedTurn > turn)).Select(p => p.Id).ToList();
            var sampled = h.Timeline.Samples.Where(s => s.Turn == turn).Select(s => s.PolityId).ToList();
            Assert.Equal(expected, sampled);
        }
        Assert.All(h.Timeline.Samples, s => Assert.Equal(h.Scalars.Count, s.Scalars.Length));
        Assert.True(h.Scalars.TryGet("test/morale", out var morale));
        Assert.All(h.Timeline.Samples, s => Assert.Equal(0.25, s.Scalars[morale.Id]));
        foreach (var p in h.Polities.Where(p => p.Alive))
        {
            var last = h.Timeline.SamplesOf(p.Id)[^1];
            Assert.Equal(h.SimulatedTurns, last.Turn);
            Assert.Equal(p.Population, last.Population);
            Assert.Equal(p.Pressure, last.Scalars[ScalarRegistry.Pressure]);
        }
    }

    [Fact]
    public void ShorterRun_RecordsThePrefixOfALongerRunsTimeline()
    {
        foreach (int seed in Seeds)
        {
            using var shortRun = Build(seed, 12);
            using var longRun = Build(seed, 30);
            for (int turn = 0; turn < 12; turn++)
                Assert.Equal(longRun.History.OwnershipAt(turn), shortRun.History.OwnershipAt(turn));
            static string Sig(PolitySample s) => $"{s.Turn}|{s.PolityId}|{s.Population:R}|{string.Join(',', s.Scalars.Select(v => v.ToString("R")))}";
            Assert.Equal(longRun.History.Timeline.Samples.Where(s => s.Turn < 12).Select(Sig), shortRun.History.Timeline.Samples.Select(Sig));
        }
    }

    [Fact]
    public void Timeline_RoundTrips_AndIsDeterministic()
    {
        using var a = Build(1234);
        using var b = Build(1234);
        Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
        var loaded = RoundTrip(a.History);
        Assert.Equal(a.History.ToBytes(), loaded.ToBytes());
        Assert.Equal(a.History.Timeline.Changes, loaded.Timeline.Changes);
        Assert.Equal(a.History.Timeline.Samples.Count, loaded.Timeline.Samples.Count);
        for (int turn = 0; turn <= a.History.SimulatedTurns; turn++)
            Assert.Equal(a.History.OwnershipAt(turn), loaded.OwnershipAt(turn));
    }

    [Fact]
    public void Respatialize_LogsItsReflood_AtTheHead()
    {
        using var w = Build(42);
        var h = w.History;
        int head = h.SimulatedTurns;
        var before = h.OwnershipAt(head - 1);
        int tile = Array.FindIndex(h.Ownership, o => o >= 0);
        int other = h.Polities.First(p => p.Alive && p.Id != h.Ownership[tile]).Id;
        h.Overrides.PinTile(tile, other);
        Assert.True(h.Respatialize(w.Map.DataStore));
        Assert.Equal(other, h.Ownership[tile]);
        Assert.Equal(h.Ownership, h.OwnershipAt(head));
        Assert.Equal(before, h.OwnershipAt(head - 1));
        Assert.Equal(h.ToBytes(), RoundTrip(h).ToBytes());
    }

    [Fact]
    public void Reset_ClearsTheTimeline()
    {
        using var w = Build(42);
        w.History.Reset();
        Assert.False(w.History.Timeline.Recorded);
        Assert.Empty(w.History.Timeline.Changes);
        Assert.Empty(w.History.Timeline.Samples);
    }

    [Fact]
    public void WithoutHistory_OwnershipAtIsTheEmptyOwnership()
    {
        using var w = new WorldBuilder().WithSize(3).WithSeed(42).WithoutHistory().Build();
        Assert.False(w.History.Timeline.Recorded);
        Assert.Empty(w.History.OwnershipAt(5));
    }

    [Fact]
    public void Load_RejectsACorruptTimeline()
    {
        void Rejects(Action<HistoryCatalog> corrupt)
        {
            using var w = Build(42);
            corrupt(w.History);
            Assert.Throws<InvalidDataException>(() => RoundTrip(w.History));
        }
        Rejects(h => h.Timeline.AddLoaded(new TerritoryChange(h.SimulatedTurns, 0, 999)));
        Rejects(h => h.Timeline.AddLoaded(new TerritoryChange(h.SimulatedTurns, h.Ownership.Length, 0)));
        Rejects(h => h.Timeline.AddLoaded(new TerritoryChange(h.SimulatedTurns + 1, 0, 0)));
        Rejects(h => h.Timeline.AddLoaded(new TerritoryChange(0, 0, 0)));
        Rejects(h => h.Timeline.BaseOwnership = new int[h.Ownership.Length - 1]);
        Rejects(h => h.Timeline.BaseOwnership[0] = 999);
        Rejects(h => h.Timeline.AddLoaded(new PolitySample { Turn = h.SimulatedTurns, PolityId = 999, Scalars = new double[1] }));
        Rejects(h => h.Timeline.AddLoaded(new PolitySample { Turn = h.SimulatedTurns, PolityId = 0, Scalars = new double[1] }));
        Rejects(h => h.Timeline.AddLoaded(new PolitySample { Turn = h.SimulatedTurns + 1, PolityId = h.Polities.Count - 1, Scalars = new double[1] }));
    }
}
