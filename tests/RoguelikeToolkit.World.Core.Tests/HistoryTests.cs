using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>P2a (history runtime, polity formation, ownership, persistence) acceptance.</summary>
public class HistoryTests
{
    private const int Size = 3;
    private const int Seed = 42;

    private static World Build(int seed = Seed, int size = Size, Action<HistoryContext>? configure = null)
    {
        var builder = new WorldBuilder().WithSize(size).WithSeed(seed).WithUserEdits();
        if (configure != null) builder.WithHistory(configure);
        return builder.Build();
    }

    private static int OwnedLandTile(World w)
    {
        var elev = w.Map.DataStore.GetSpan<ElevationInfo>();
        for (int i = 0; i < w.TileCount; i++)
        {
            if (w.History.OwnerOf(i) >= 0 && elev[i].Height >= 0f) return i;
        }
        throw new InvalidOperationException("Test world has no owned land tile.");
    }

    [Fact]
    public void StageOrders_MatchPlan()
    {
        Assert.Equal(26, StageOrder.CultureSeed);
        Assert.Equal(27, StageOrder.PolityForm);
        Assert.Equal(28, StageOrder.HistoryEvents);
        Assert.Equal(91, StageOrder.CityHistoryProject);
    }

    [Fact]
    public void SameSeed_SameHistoryBytes()
    {
        using var a = Build();
        using var b = Build();
        Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
    }

    [Fact]
    public void DifferentSeed_DifferentHistory()
    {
        using var a = Build();
        using var b = Build(seed: 43);
        Assert.NotEqual(a.History.ToBytes(), b.History.ToBytes());
    }

    [Fact]
    public void Build_FoundsPolities_WithFoundingEventsAndProjections()
    {
        using var w = Build();
        var history = w.History;
        Assert.True(history.CultureSeeded);
        Assert.True(history.PolitiesFormed);
        Assert.NotEmpty(history.Polities);
        int foundedAtZero = history.Polities.Count(p => p.FoundedTurn == 0);
        Assert.Equal(foundedAtZero, history.Events.Count(e => e.Kind == HistoryEventKind.Founding));
        // Collapse successors may take over a standing settlement instead of founding a new seat.
        Assert.True(history.Settlements.Count(s => s.Role == SettlementRole.Capital) <= history.Polities.Count);
        Assert.True(history.Sites.Count >= history.Polities.Count);
        Assert.Equal(29, history.SimulatedTurns);
        foreach (var polity in history.Polities)
        {
            if (polity.Alive) Assert.Equal(polity.Id, history.OwnerOf(polity.CapitalTile));
            if (polity.FoundedTurn == 0)
            {
                var founding = history.EventsOf(polity.Id).Single(e => e.Kind == HistoryEventKind.Founding);
                Assert.Equal(0, founding.Turn);
                Assert.Equal(polity.CapitalTile, founding.TileIndex);
                Assert.Contains(polity.Form.ToString(), founding.Tags);
                Assert.Contains(polity.FolkKey, founding.Tags);
                Assert.Contains(polity.CultureKey, founding.Tags);
            }
            else
            {
                // Born in a rebellion, or as a collapse successor (which has no rebellion event).
                var rebellion = history.Events.SingleOrDefault(e => e.Kind == HistoryEventKind.Rebellion
                    && e.PolityId == polity.Id && e.Turn == polity.FoundedTurn);
                if (rebellion != null) Assert.Equal(polity.CapitalTile, rebellion.TileIndex);
                else Assert.Contains(history.Events, e => e.Kind == HistoryEventKind.PolityCollapsed && e.Tags.Contains($"successor:{polity.Id}"));
            }
            // An absorber also holds the seats of the polities it took, so it may list more than one capital-role city.
            if (polity.Alive && !history.Events.Any(e => e.Kind == HistoryEventKind.PolityCollapsed && e.Tags.Contains($"successor:{polity.Id}")))
                Assert.Contains(history.CitiesOf(polity.Id), c => c.Role == SettlementRole.Capital);
        }
    }

    [Fact]
    public void History_RngScheme_IsDeterministicAndPurposeScoped()
    {
        var first = HistoryRng.Create(Seed, "PolityForm", 0, 0, "capital");
        var second = HistoryRng.Create(Seed, "PolityForm", 0, 0, "capital");
        Assert.Equal(first.NextDouble(), second.NextDouble());
        Assert.Equal(HistoryRng.StableHash("capital"), HistoryRng.StableHash("capital"));
        Assert.NotEqual(HistoryRng.StableHash("capital"), HistoryRng.StableHash("peoples"));
    }

    [Fact]
    public void Context_Budgets_ResetEachTurn()
    {
        var context = new HistoryContext { MaxDecisionsPerTurn = 2, MaxEmergencesPerTurn = 1 };
        context.BeginTurn(0);
        Assert.True(context.TrySpendDecision());
        Assert.True(context.TrySpendDecision());
        Assert.False(context.TrySpendDecision());
        Assert.True(context.TrySpendEmergence());
        Assert.False(context.TrySpendEmergence());
        context.BeginTurn(1);
        Assert.True(context.TrySpendDecision());
        Assert.True(context.TrySpendEmergence());
    }

    [Fact]
    public void AppendEvent_EnforcesPerTurnBudget()
    {
        var history = new HistoryCatalog(Seed);
        history.SyncBudgets(2, 4096);
        Assert.True(history.AppendEvent(HistoryEventKind.Founding, 0, 0, 1, new[] { "a" }));
        Assert.True(history.AppendEvent(HistoryEventKind.Founding, 0, 1, 2, new[] { "b" }));
        Assert.False(history.AppendEvent(HistoryEventKind.Founding, 0, 2, 3, new[] { "c" }));
        Assert.Equal(2, history.Events.Count);
        Assert.Equal(1, history.DroppedEvents);
        Assert.True(history.AppendEvent(HistoryEventKind.Founding, 1, 2, 3, new[] { "c" }));
        Assert.Equal(3, history.Events.Count);
    }

    [Fact]
    public void LogCap_CompactsOldestNonFoundingFirst()
    {
        var history = new HistoryCatalog(Seed);
        history.SyncBudgets(64, 4);
        history.AppendEvent(HistoryEventKind.Founding, 0, 0, 1, new[] { "keep" });
        for (int i = 0; i < 5; i++)
            history.AppendEvent(HistoryEventKind.SiteLost, 1, 0, 10 + i, new[] { "drop" });
        Assert.Equal(4, history.Events.Count);
        Assert.Contains(history.Events, e => e.Kind == HistoryEventKind.Founding);
        Assert.True(history.Compactions >= 1);
    }

    [Fact]
    public void RoundTrip_PreservesCatalogBytes()
    {
        using var w = Build();
        w.History.Overrides.RenamePolity(0, "Test Name");
        byte[] bytes = w.History.ToBytes();
        HistoryCatalog loaded;
        using (var stream = new MemoryStream(bytes, writable: false))
            loaded = HistoryCatalog.Load(stream);
        Assert.Equal(bytes, loaded.ToBytes());
        Assert.Equal("Test Name", loaded.DisplayNameOf(0));
        Assert.Equal(w.History.Polities.Count, loaded.Polities.Count);
        Assert.Equal(w.History.Ownership, loaded.Ownership);
    }

    [Fact]
    public void Load_RejectsOutOfRangePolityReference()
    {
        using var w = Build();
        w.History.Faiths.Add(new HistoryFaith { PolityId = w.History.Polities.Count + 5, Form = "x" });
        byte[] bytes = w.History.ToBytes();
        using var stream = new MemoryStream(bytes, writable: false);
        Assert.Throws<InvalidDataException>(() => HistoryCatalog.Load(stream));
    }

    [Fact]
    public void Load_RejectsBadMagicAndVersion()
    {
        using var w = Build();
        byte[] bytes = w.History.ToBytes();
        bytes[0] = (byte)'X';
        using (var stream = new MemoryStream(bytes, writable: false))
            Assert.Throws<InvalidDataException>(() => HistoryCatalog.Load(stream));
        using (var stream = new MemoryStream(new byte[] { 1, 2, 3 }, writable: false))
            Assert.Throws<InvalidDataException>(() => HistoryCatalog.Load(stream));
    }

    [Fact]
    public void HistoricalContext_ServesCapitalSites_NoModifiers()
    {
        using var w = Build();
        var polity = w.History.Polities[0];
        var position = w.Map.DataStore.GetGeoCoord(polity.CapitalTile);
        var sites = w.History.GetSitesNear(position, 1.0).ToList();
        Assert.Contains(sites, s => s.TileIndex == polity.CapitalTile);
        Assert.DoesNotContain(sites, s => s.DangerDelta != 0f || s.HabitabilityDelta != 0f);
        Assert.False(w.History.TryGetTileModifier(polity.CapitalTile, out _));
    }

    [Fact]
    public void WithoutHistory_BuildsEmptyCatalog()
    {
        using var w = new WorldBuilder().WithSize(Size).WithSeed(Seed).WithoutHistory().Build();
        Assert.NotNull(w.History);
        Assert.Empty(w.History.Polities);
        Assert.Empty(w.History.Events);
        Assert.False(w.History.CultureSeeded);
    }

    [Fact]
    public void Recompute_WithoutEdits_KeepsHistoryWithoutFork()
    {
        using var w = Build();
        int[] ownership = (int[])w.History.Ownership.Clone();
        int events = w.History.Events.Count;
        w.Recompute();
        Assert.True(w.History.Stale);
        Assert.Null(w.History.ForkedAtTurn);
        Assert.Equal(ownership, w.History.Ownership);
        Assert.Equal(events, w.History.Events.Count);
    }

    [Fact]
    public void Recompute_NullEdit_DoesNotFork()
    {
        using var w = Build();
        int tile = OwnedLandTile(w);
        w.Edits[tile].HeightDelta = 0f;
        w.Recompute();
        Assert.Null(w.History.ForkedAtTurn);
        Assert.DoesNotContain(w.History.Events, e => e.Kind == HistoryEventKind.Fork);
    }

    [Fact]
    public void Recompute_DrowningEdit_ForksTimeline()
    {
        using var w = Build();
        int tile = OwnedLandTile(w);
        Assert.True(tile >= 0);
        w.Edits[tile].Flags = UserEditFlags.PinHeight;
        w.Edits[tile].PinHeight = -1f;
        w.Recompute();
        Assert.True(w.History.Stale);
        Assert.NotNull(w.History.ForkedAtTurn);
        Assert.Equal(w.History.SimulatedTurns, w.History.ForkedAtTurn);
        Assert.Contains(w.History.Events, e => e.Kind == HistoryEventKind.Fork);
        Assert.Equal(-1, w.History.OwnerOf(tile));
    }

    [Fact]
    public void PinOnACapitalTile_IsIgnored_SoNoPolityIsLeftLandless()
    {
        using var w = Build();
        Assert.True(w.History.Polities.Count >= 2);
        int victim = 0, thief = 1;
        int capital = w.History.Polities[victim].CapitalTile;
        w.History.Overrides.PinTile(capital, thief);
        w.Recompute();
        Assert.Equal(victim, w.History.OwnerOf(capital));
        Assert.Contains(w.History.Ownership, o => o == victim);
    }

    [Fact]
    public void Overrides_SurviveRecompute_AndPinsWin()
    {
        using var w = Build();
        Assert.True(w.History.Polities.Count >= 2, "Seed 42 size 3 must form at least two polities for the pin test.");
        int tile = OwnedLandTile(w);
        int owner = w.History.OwnerOf(tile);
        int other = w.History.Polities.First(p => p.Alive && p.Id != owner).Id; // a pin on an ended polity is inert
        w.History.Overrides.RenamePolity(other, "Pinned Ones");
        w.History.Overrides.PinTile(tile, other);
        w.Recompute();
        Assert.Equal("Pinned Ones", w.History.DisplayNameOf(other));
        Assert.Equal(other, w.History.OwnerOf(tile));
        Assert.NotNull(w.History.ForkedAtTurn);
        int forks = w.History.Events.Count(e => e.Kind == HistoryEventKind.Fork);
        w.Recompute();
        Assert.Equal(other, w.History.OwnerOf(tile));
        Assert.Equal("Pinned Ones", w.History.DisplayNameOf(other));
        Assert.Equal(forks, w.History.Events.Count(e => e.Kind == HistoryEventKind.Fork));
    }

    [Fact]
    public void RegenerateHistory_ClearsStaleAndFork_Deterministically()
    {
        using var a = Build();
        using var b = Build();
        int tileA = OwnedLandTile(a);
        int tileB = OwnedLandTile(b);
        a.Edits[tileA].Flags = UserEditFlags.PinHeight;
        a.Edits[tileA].PinHeight = -1f;
        b.Edits[tileB].Flags = UserEditFlags.PinHeight;
        b.Edits[tileB].PinHeight = -1f;
        a.Recompute();
        b.Recompute();
        Assert.NotNull(a.History.ForkedAtTurn);
        a.RegenerateHistory();
        b.RegenerateHistory();
        Assert.False(a.History.Stale);
        Assert.Null(a.History.ForkedAtTurn);
        Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
    }

    [Fact]
    public void HistoryPerf_FullBuildWithinBudget()
    {
        var started = DateTime.UtcNow;
        using var w = new WorldBuilder().WithSize(4).WithSeed(7).Build();
        var elapsed = DateTime.UtcNow - started;
        Assert.True(w.History.PolitiesFormed);
        Assert.True(elapsed < TimeSpan.FromSeconds(120), $"Size-4 build with history took {elapsed}.");
    }
}
