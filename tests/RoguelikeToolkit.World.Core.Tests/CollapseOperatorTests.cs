using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>M3.6: a polity can collapse into successors; territory, settlements and pins follow.</summary>
public class CollapseOperatorTests
{
    private sealed class Script : IHistoryOperator
    {
        private readonly Func<TurnView, IEnumerable<Proposal>> _emit;
        public Script(Func<TurnView, IEnumerable<Proposal>> emit) => _emit = emit;
        public string Name => "Script";
        public int Order => 1000;
        public void Propose(TurnView view, ProposalSink sink) { foreach (var p in _emit(view)) sink.Emit(p); }
    }

    private static World Bare()
        => new WorldBuilder().WithSize(3).WithSeed(42).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();

    private static List<int> OwnedTiles(World w, int polity, int count)
    {
        var tiles = new List<int>();
        for (int t = 0; t < w.History.Ownership.Length && tiles.Count < count; t++)
            if (w.History.Ownership[t] == polity && t != w.History.Polities[polity].CapitalTile) tiles.Add(t);
        return tiles;
    }

    private static List<int> TwoTiles(World w)
    {
        var owned = OwnedTiles(w, 0, int.MaxValue);
        Assert.True(owned.Count >= 4, "polity 0 owns too little land for the test");
        return new List<int> { owned[1], owned[^1] };
    }

    private static void Split(World w, IReadOnlyList<int> tiles, HistoryContext? ctx = null, int turn = 30)
        => new HistoryDriver()
            .AddOperator(new Script(_ => new Proposal[] { new Proposal.SplitPolity { EntityId = 0, SuccessorTiles = tiles } }))
            .RunTurn(w.Map, w.History, ctx ?? new HistoryContext(), turn);

    [Fact]
    public void Split_EndsTheParent_FoundsSuccessors_AndHandsOverTheLand()
    {
        using var w = Bare();
        var tiles = TwoTiles(w);
        int before = w.History.Polities.Count;
        Split(w, tiles);

        var parent = w.History.Polities[0];
        Assert.False(parent.Alive);
        Assert.Equal(30, parent.EndedTurn);
        Assert.Equal("collapse", w.History.Strings.NameOf(parent.EndCause));
        Assert.Equal(before + 2, w.History.Polities.Count);
        for (int i = 0; i < 2; i++)
        {
            var successor = w.History.Polities[before + i];
            Assert.Equal(0, successor.Parent);
            Assert.Equal(tiles[i], successor.CapitalTile);
            Assert.Equal(parent.FolkKey, successor.FolkKey);
            Assert.Equal(before + i, w.History.Ownership[tiles[i]]);
        }
        Assert.DoesNotContain(0, w.History.Ownership);
        Assert.All(w.History.Settlements.Where(s => !s.Ended), s => Assert.NotEqual(0, s.PolityId));
        var seat = w.History.Settlements[0];
        Assert.True(seat.Ended);
        Assert.Equal("razed", seat.EndCause);
        Assert.Contains(w.History.Events, e => e.Kind == HistoryEventKind.PolityCollapsed && e.Tags.Contains($"successor:{before}"));
    }

    [Fact]
    public void Split_Refuses_OneSuccessor_ForeignTiles_AndAnEndedParent()
    {
        using var w = Bare();
        var tiles = OwnedTiles(w, 0, 3);
        Split(w, new[] { tiles[0] });
        Assert.True(w.History.Polities[0].Alive);

        Split(w, new[] { tiles[0], tiles[1] }, new HistoryContext { MaxSuccessors = 1 });
        Assert.True(w.History.Polities[0].Alive);

        Split(w, tiles);
        Assert.False(w.History.Polities[0].Alive);
        int count = w.History.Polities.Count;
        Split(w, tiles, turn: 31); // the parent is already gone
        Assert.Equal(count, w.History.Polities.Count);
    }

    [Fact]
    public void PinOnAnEndedPolity_IsKeptButInert()
    {
        using var w = Bare();
        var tiles = TwoTiles(w);
        Split(w, tiles);
        w.History.Overrides.PinTile(OwnedTiles(w, w.History.Polities.Count - 1, 1)[0], 0);

        w.History.Respatialize(w.Map.DataStore);

        Assert.Single(w.History.Overrides.PinnedTiles);
        Assert.DoesNotContain(0, w.History.Ownership);
    }

    [Fact]
    public void FullRun_Collapses_KeepEveryInvariant_AndAreDeterministic()
    {
        int collapses = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            using var w = new WorldBuilder().WithSize(3).WithSeed(seed).WithHistory().Build();
            var h = w.History;
            foreach (var parent in h.Polities.Where(p => !p.Alive))
            {
                collapses++;
                Assert.DoesNotContain(parent.Id, h.Ownership);
                Assert.All(h.Settlements.Where(s => !s.Ended), s => Assert.NotEqual(parent.Id, s.PolityId));
                Assert.All(h.HolySites, s => Assert.NotEqual(parent.Id, s.PolityId));
                var successors = h.Polities.Where(p => p.Parent == parent.Id).ToList();
                Assert.InRange(successors.Count, 2, CollapseOperator.MaxPerCollapse + 1);
                Assert.Contains(successors, s => h.Ownership.Contains(s.Id));
            }
            using var again = new WorldBuilder().WithSize(3).WithSeed(seed).WithHistory().Build();
            Assert.Equal(h.Polities.Count, again.History.Polities.Count);
            Assert.True(h.Ownership.AsSpan().SequenceEqual(again.History.Ownership));
        }
        Assert.True(collapses > 0, "no collapse in 20 seeds; the trigger is unreachable");
    }
}
