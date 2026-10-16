using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.History;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>P2b (conflict operators behind the turn driver) acceptance.</summary>
public class HistoryOperatorsTests
{
    private const int Size = 3;
    private const int Seed = 42;

    private readonly ITestOutputHelper _output;

    /// <summary>Builds the output helper.</summary>
    /// <param name="output">xUnit output sink for the diagnostic probe.</param>
    public HistoryOperatorsTests(ITestOutputHelper output) => _output = output;

    private static World Build(int seed = Seed, int size = Size, Action<HistoryContext>? configure = null)
    {
        var builder = new WorldBuilder().WithSize(size).WithSeed(seed).WithUserEdits();
        if (configure != null) builder.WithHistory(configure);
        return builder.Build();
    }

    private static string TagPrefix(IReadOnlyList<string> tags, string prefix)
    {
        foreach (string tag in tags)
            if (tag.StartsWith(prefix, StringComparison.Ordinal)) return tag;
        throw new InvalidOperationException($"No '{prefix}' tag in [{string.Join(",", tags)}].");
    }

    [Fact]
    public void Probe_WorldSummary()
    {
        using var w = Build();
        var h = w.History;
        foreach (var kind in Enum.GetValues<HistoryEventKind>())
            _output.WriteLine($"{kind}: {h.Events.Count(e => e.Kind == kind)}");
        _output.WriteLine($"polities: {h.Polities.Count} roads: {h.Roads.Count} sites: {h.Sites.Count} settlements: {h.Settlements.Count}");
        foreach (var p in h.Polities)
            _output.WriteLine($"polity {p.Id} {p.Form} {p.FolkKey}/{p.CultureKey} overlay='{p.CultureOverlay}' pop={p.Population:F1} pressure={p.Pressure:F3} gen={p.RulerGeneration} open={p.OpennessDrift} founded={p.FoundedTurn}");
        _output.WriteLine($"severed: {h.Roads.Count(r => r.Severed)} dropped: {h.DroppedEvents} compactions: {h.Compactions}");
    }

    [Fact]
    public void StageOrders_SitBehindTheDriver()
    {
        Assert.Equal(30, StageOrder.TradeRoad);
        Assert.Equal(32, StageOrder.Population);
        Assert.Equal(50, StageOrder.War);
        Assert.Equal(60, StageOrder.Conquest);
        Assert.Equal(70, StageOrder.Rebellion);
        Assert.Equal(80, StageOrder.Exchange);
    }

    [Fact]
    public void Demography_GenerationTurns_MatchPlan()
    {
        Assert.Equal(3, HistoryDemography.GenerationTurns(80, 10));
        Assert.Equal(8, HistoryDemography.GenerationTurns(350, 10));
    }

    [Fact]
    public void Demography_Logistics_StaysBounded()
    {
        double rate = HistoryDemography.AnnualToTurnRate(0.05, 10);
        Assert.InRange(rate, 0.3, 0.5);
        double pop = HistoryDemography.PopulationAtTurn(10000, rate, 29);
        Assert.InRange(pop / 10000, 0.5, 1.0);
        Assert.Equal(0, HistoryDemography.PressureOf(1000, 10000));
        Assert.Equal(1, HistoryDemography.PressureOf(10000, 10000));
        Assert.Equal(0, HistoryDemography.PressureOf(100, 0));
    }

    [Fact]
    public void OperatorBudgets_ArePerOperatorPerTurn()
    {
        var context = new HistoryContext { MaxDecisionsPerTurn = 1, MaxEmergencesPerTurn = 1 };
        Assert.True(context.TrySpendOperatorDecision("War", 1));
        Assert.False(context.TrySpendOperatorDecision("War", 1));
        Assert.True(context.TrySpendOperatorDecision("War", 2));
        Assert.True(context.TrySpendOperatorDecision("Conquest", 1));
        Assert.True(context.TrySpendOperatorEmergence("TradeRoad", 1));
        Assert.False(context.TrySpendOperatorEmergence("TradeRoad", 1));
        context.ResetOperatorBudget("War");
        Assert.True(context.TrySpendOperatorDecision("War", 1));
        Assert.False(context.TrySpendOperatorDecision("Conquest", 1));
        context.ClearOperatorBudgets();
        Assert.True(context.TrySpendOperatorEmergence("TradeRoad", 1));
    }

    [Fact]
    public void SameSeed_SameOperatorBytes()
    {
        using var a = Build();
        using var b = Build();
        Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
    }

    [Fact]
    public void Population_TicksForEveryPolity()
    {
        using var w = Build();
        foreach (var polity in w.History.Polities.Where(p => p.Alive))
        {
            Assert.True(polity.Population > 0, $"Polity {polity.Id} has no population.");
            Assert.InRange(polity.Pressure, 0, HistoryDemography.MaxPressure);
            Assert.True(polity.RulerGeneration >= 0, $"Polity {polity.Id} has negative generations.");
        }
    }

    [Fact]
    public void Roads_AreLandBound_AndCutsMatchSevers()
    {
        using var w = Build();
        var history = w.History;
        var cutRoutes = new HashSet<string>();
        foreach (var entry in history.Events)
        {
            if (entry.Kind != HistoryEventKind.RoadCut) continue;
            cutRoutes.Add(TagPrefix(entry.Tags, "route:"));
        }
        var severedRoutes = new HashSet<string>();
        foreach (var road in history.Roads)
        {
            Assert.True(road.RouteA < road.RouteB, $"Route endpoints unordered on {road.TileA}-{road.TileB}.");
            Assert.True(road.FoundedTurn >= 1 && road.FoundedTurn < 30, $"Route founded off-span at turn {road.FoundedTurn}.");
            if (road.Severed)
            {
                Assert.True(road.SeveredTurn >= road.FoundedTurn, "Route severed before it opened.");
                severedRoutes.Add(HistoryDemography.RouteTag(road.RouteA, road.RouteB));
            }
        }
        Assert.Equal(severedRoutes, cutRoutes);
    }

    [Fact]
    public void Wars_HaveBattlefields_AndPressureBothSides()
    {
        using var w = Build();
        var history = w.History;
        var wars = history.Events.Where(e => e.Kind == HistoryEventKind.War).ToList();
        foreach (var war in wars)
        {
            int defender = int.Parse(TagPrefix(war.Tags, "defender:").Substring(9));
            Assert.True(history.Sites.Any(s => s.TileIndex == war.TileIndex) || history.Scars.Any(s => s.TileIndex == war.TileIndex && s.Kind == HistoryScar.Battlefield));
            Assert.True(history.TryGetTileModifier(war.TileIndex, out var modifier));
            Assert.Equal(HistoryCatalog.WarDangerDelta, modifier.DangerDelta);
            Assert.True(history.Polities[war.PolityId].Pressure > 0, "Attacker carries no pressure.");
            Assert.True(history.Polities[defender].Pressure > 0, "Defender carries no pressure.");
        }
    }

    [Fact]
    public void Conquests_OnlyOverlayCultures()
    {
        using var w = Build();
        var history = w.History;
        foreach (var entry in history.Events.Where(e => e.Kind == HistoryEventKind.Conquest))
        {
            int loser = int.Parse(TagPrefix(entry.Tags, "loser:").Substring(6));
            int winner = int.Parse(TagPrefix(entry.Tags, "winner:").Substring(7));
            // A loser conquered twice renders through its latest conqueror; earlier entries stay in the log.
            var latest = history.Events.Last(e => e.Kind == HistoryEventKind.Conquest
                && TagPrefix(e.Tags, "loser:") == $"loser:{loser}");
            if (ReferenceEquals(latest, entry))
                Assert.Equal(history.Polities[winner].CultureKey, history.Polities[loser].CultureOverlay);
            Assert.Contains(history.Polities[loser].CultureKey, entry.Tags[^1] == history.Polities[winner].CultureKey
                ? new[] { history.Polities[loser].CultureKey }
                : new[] { entry.Tags[^1] });
            Assert.Equal(winner, entry.PolityId);
        }
    }

    [Fact]
    public void Exchange_OnlyRunsOnActiveRoutes()
    {
        // A bare world: the standard history would already have run these turns, and replaying them over it breaks the route spans.
        using var w = new WorldBuilder().WithSize(Size).WithSeed(Seed).WithUserEdits().WithHistoryDriver(d => d.Clear()).Build();
        new HistoryDriver()
            .AddOperator(new PopulationOperator())
            .AddOperator(new TradeRoadOperator())
            .AddOperator(new WarOperator())
            .AddOperator(new ExchangeOperator())
            .Apply(w.Map, w.History, new HistoryContext());
        var history = w.History;
        var routes = new Dictionary<string, (int Founded, int Severed)>();
        foreach (var road in history.Roads)
        {
            string tag = HistoryDemography.RouteTag(road.RouteA, road.RouteB);
            if (!routes.TryGetValue(tag, out var span))
            {
                routes[tag] = (road.FoundedTurn, road.Severed ? road.SeveredTurn : int.MaxValue);
            }
            else
            {
                routes[tag] = (
                    Math.Min(span.Founded, road.FoundedTurn),
                    road.Severed ? Math.Min(span.Severed, road.SeveredTurn) : span.Severed);
            }
        }
        foreach (var entry in history.Events.Where(e => e.Kind == HistoryEventKind.Exchange))
        {
            string tag = TagPrefix(entry.Tags, "route:");
            Assert.True(routes.TryGetValue(tag, out var span), $"Exchange on unknown route {tag}.");
            Assert.True(entry.Turn >= span.Founded, $"Exchange before route {tag} opened.");
            Assert.True(entry.Turn <= span.Severed, $"Exchange after route {tag} was cut."); // same-turn cut and exchange are simultaneous under snapshot reads
        }
        foreach (var polity in history.Polities)
            Assert.InRange(polity.OpennessDrift, -HistoryDemography.MaxOpennessDrift, HistoryDemography.MaxOpennessDrift);
        Assert.Contains(history.Events, e => e.Kind == HistoryEventKind.Exchange);
    }

    [Fact]
    public void StarvedWar_LeavesRoadBuildingUntouched()
    {
        // Colonies split population by catchment and wars now sack settlements, so demography is not compared.
        using var full = Build(configure: ctx => ctx.MaxSettlementsPerPolity = 1);
        using var starved = Build(configure: ctx => { ctx.MaxDecisionsPerTurn = 0; ctx.MaxSettlementsPerPolity = 1; });
        Assert.DoesNotContain(starved.History.Events, e => e.Kind == HistoryEventKind.War);
        Assert.DoesNotContain(starved.History.Events, e => e.Kind == HistoryEventKind.Conquest);
        var fullBuilt = full.History.Events
            .Where(e => e.Kind == HistoryEventKind.RoadBuilt)
            .SelectMany(e => e.Tags).OrderBy(t => t).ToList();
        var starvedBuilt = starved.History.Events
            .Where(e => e.Kind == HistoryEventKind.RoadBuilt)
            .SelectMany(e => e.Tags).OrderBy(t => t).ToList();
        Assert.Equal(fullBuilt, starvedBuilt);
        Assert.Equal(full.History.Polities.Count, starved.History.Polities.Count);
    }

    [Fact]
    public void RoundTrip_PreservesOperatorState()
    {
        using var w = Build();
        byte[] bytes = w.History.ToBytes();
        HistoryCatalog loaded;
        using (var stream = new MemoryStream(bytes, writable: false))
            loaded = HistoryCatalog.Load(stream);
        Assert.Equal(bytes, loaded.ToBytes());
        Assert.Equal(w.History.Roads.Count, loaded.Roads.Count);
        for (int i = 0; i < w.History.Polities.Count; i++)
        {
            Assert.Equal(w.History.Polities[i].Population, loaded.Polities[i].Population);
            Assert.Equal(w.History.Polities[i].Pressure, loaded.Polities[i].Pressure);
            Assert.Equal(w.History.Polities[i].CultureOverlay, loaded.Polities[i].CultureOverlay);
            Assert.Equal(w.History.Polities[i].RulerGeneration, loaded.Polities[i].RulerGeneration);
        }
    }
}
