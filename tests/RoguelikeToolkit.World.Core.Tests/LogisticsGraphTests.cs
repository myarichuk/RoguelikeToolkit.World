using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X5 (docs/HISTORY_EXPANSION_PLAN.md §4): the logistics network over a real world's settlements, its costs and the movers' rules,
/// and winter quarters in the battle resolver.
/// </summary>
public class LogisticsGraphTests
{
    private readonly ITestOutputHelper _output;

    public LogisticsGraphTests(ITestOutputHelper output) => _output = output;

    private static readonly PeoplesStore Store = PeoplesStore.Load();
    private static readonly WarfareProfile Profile = WarfareProfile.From(Store);

    private static World Build(int seed = 9)
        => new WorldBuilder().WithSize(3).WithSeed(seed).WithHistory(c => { c.Features = HistoryFeatures.All; c.TurnCount = 30; }).Build();

    private static TurnView View(World world) => new(world.Map, world.History, new HistoryContext { Features = HistoryFeatures.All }, world.History.SimulatedTurns);

    /// <summary>A profile over a fresh copy of the shipped data, edited by <paramref name="edit"/>.</summary>
    private static WarfareProfile Edited(Action<PeoplesStore> edit)
    {
        var store = PeoplesStore.Load();
        edit(store);
        return WarfareProfile.From(store);
    }

    [Fact]
    public void ShippedLogisticsData_LoadsAndNamesOnlyKnownThings()
    {
        Assert.Empty(Profile.UnknownKeys);
        Assert.True(Store.Logistics.ContainsKey("default"));
        Assert.All(Profile.TerrainCost, c => Assert.True(c > 0));
        // Who sails is data: the longhouse and marsh peoples do, the deep clans do not.
        Assert.True(Profile.Sails(PolityForm.Kingdom, Profile.WayOf("dogfolk_pack")));
        Assert.False(Profile.Sails(PolityForm.Kingdom, Profile.WayOf("dwarven_clan")));
        Assert.True(Profile.Sails(PolityForm.MerchantRepublic, Profile.WayOf("dwarven_clan")), "a merchant republic sails whatever its people");
    }

    [Fact]
    public void Links_AreSymmetric_AndCostAtLeastTheirCheapestRate()
    {
        using var world = Build();
        var graph = LogisticsGraph.Build(View(world), Profile);
        var data = Profile.Logistics;
        var vectors = world.Map.DataStore.GetTileVectors();
        Assert.True(graph.NodeCount > 3);
        var kinds = new Dictionary<LogisticsGraph.LinkKind, int>();
        double minTerrain = Profile.TerrainCost.Min();
        for (int u = 0; u < graph.NodeCount; u++)
            foreach (var link in graph.LinksOf(u))
            {
                kinds[link.Kind] = kinds.GetValueOrDefault(link.Kind) + 1;
                Assert.Contains(graph.LinksOf(link.To), back => back.To == u && back.Kind == link.Kind && back.Cost == link.Cost);
                double deg = LogisticsGraph.Degrees(vectors[graph.TileOf(u)], vectors[graph.TileOf(link.To)]);
                double floor = link.Kind switch
                {
                    LogisticsGraph.LinkKind.Overland => deg * data.OverlandCost * minTerrain,
                    LogisticsGraph.LinkKind.River => deg * data.RiverCost,
                    LogisticsGraph.LinkKind.Sea => deg * data.SeaCost,
                    _ => deg * data.RoadCost * (1 - data.WaystationRelief * data.WaystationsCounted), // a road is at least as long as the crow flies
                };
                Assert.True(link.Cost >= floor - 1e-9, $"{link.Kind} link {u}->{link.To} costs {link.Cost:F2} under {floor:F2}");
            }
        _output.WriteLine("links: " + string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
        Assert.True(kinds.GetValueOrDefault(LogisticsGraph.LinkKind.Overland) > 0);
    }

    [Fact]
    public void Costs_StartAtTheSources_AndObeyEveryOwnLink()
    {
        using var world = Build();
        var view = View(world);
        var graph = LogisticsGraph.Build(view, Profile);
        foreach (var polity in view.Polities.Where(p => p.Alive))
        {
            var cost = graph.CostsFrom(polity.Id);
            Assert.Same(cost, graph.CostsFrom(polity.Id)); // cached
            for (int u = 0; u < graph.NodeCount; u++)
            {
                Assert.True(cost[u] >= 0);
                if (graph.OwnerOf(u) != polity.Id) continue;
                var s = view.Settlements[graph.SettlementOf(u)];
                double source = s.Role == SettlementRole.Waystation ? Profile.Logistics.WaystationSource : Profile.SourceCost(s.Tier);
                Assert.True(cost[u] <= source + 1e-9, $"own node {u} costs {cost[u]} over its source {source}");
                // Between two own nodes nothing is foreign: the path is never dearer than the link.
                foreach (var link in graph.LinksOf(u))
                    if (graph.OwnerOf(link.To) == polity.Id && (link.Kind != LogisticsGraph.LinkKind.Sea || Profile.Sails(polity.Form, Profile.WayOf(polity.CultureKey))))
                        Assert.True(cost[link.To] <= cost[u] + link.Cost + 1e-9);
            }
        }
    }

    [Fact]
    public void TheTarget_IsExempt_AndAnExcludedPlaceFeedsNothing()
    {
        using var world = Build();
        var view = View(world);
        var graph = LogisticsGraph.Build(view, Profile);
        var alive = view.Polities.Where(p => p.Alive).Select(p => p.Id).ToList();
        foreach (int mover in alive)
            foreach (int target in alive.Where(t => t != mover))
            {
                var plain = graph.CostsFrom(mover);
                var toward = graph.CostsFrom(mover, target);
                for (int u = 0; u < graph.NodeCount; u++) Assert.True(toward[u] <= plain[u] + 1e-9, "heading for a polity never makes its land dearer");
            }
        int own = Enumerable.Range(0, graph.NodeCount).First(u => graph.OwnerOf(u) >= 0);
        var excluded = graph.CostsFrom(graph.OwnerOf(own), -1, graph.SettlementOf(own));
        Assert.True(double.IsPositiveInfinity(excluded[own]));
    }

    [Fact]
    public void Sailing_UnsafeRoads_AndForeignLand_MoveTheCosts()
    {
        using var world = Build();
        var view = View(world);
        var landlubbers = Edited(s => { foreach (var w in s.Warfare.Values) w.Seafaring = 0; s.Logistics["default"].SeaForms.Clear(); });
        var sailors = Edited(s => { foreach (var w in s.Warfare.Values) w.Seafaring = 100; });
        var safeRoads = Edited(s => s.Logistics["default"].RoadUnsafe = 0);
        var openBorders = Edited(s => { s.Logistics["default"].ForeignCost = 1; s.Logistics["default"].HostileCost = 1; });
        var dry = LogisticsGraph.Build(view, landlubbers);
        var wet = LogisticsGraph.Build(view, sailors);
        var safe = LogisticsGraph.Build(view, safeRoads);
        var open = LogisticsGraph.Build(view, openBorders);
        var shipped = LogisticsGraph.Build(view, Profile);
        int cheaperBySea = 0;
        foreach (var polity in view.Polities.Where(p => p.Alive))
        {
            var d = dry.CostsFrom(polity.Id);
            var w = wet.CostsFrom(polity.Id);
            var s = safe.CostsFrom(polity.Id);
            var o = open.CostsFrom(polity.Id);
            var x = shipped.CostsFrom(polity.Id);
            for (int u = 0; u < d.Length; u++)
            {
                Assert.True(w[u] <= d[u] + 1e-9);
                if (w[u] < d[u] - 1e-9) cheaperBySea++;
                Assert.True(s[u] <= x[u] + 1e-9, "a safer road is never dearer");
                Assert.True(o[u] <= x[u] + 1e-9, "open borders are never dearer");
            }
        }
        _output.WriteLine($"nodes reached cheaper by sea: {cheaperBySea}");
    }

    [Fact]
    public void WinterQuarters_SaveAStarvedWinterCampaign_AtACost()
    {
        var way = Profile.WayOf("human_common") with { CampaignSeasons = new double[] { 0, 0, 0, 1 } };
        var a = new BattleResolver.Side(0, 1000, way);
        var d = new BattleResolver.Side(1, 600, Profile.WayOf("human_common"));
        // A far field across open ground in a hard winter: supply runs short.
        var field = new BattleResolver.Field(0, Profile.TerrainIndex("open"), false, 0.1, 30, 140, ReachScale: Profile.Logistics.ReachCost);
        int averted = 0, avertedQ = 0, wintered = 0;
        for (int k = 0; k < 2000; k++)
        {
            Func<string, Rng> draws = purpose => HistoryRng.Create(5, "winter-test", 0, k, purpose);
            var plain = BattleResolver.Resolve(Profile, a, d, new[] { field }, draws, 10);
            var quartered = BattleResolver.Resolve(Profile, a, d, new[] { field }, draws, 10, winterQuarters: true);
            Assert.False(plain.Wintered);
            Assert.Equal(HistorySeason.Winter, plain.Season);
            if (plain.Outcome == BattleOutcome.Averted) averted++;
            if (quartered.Outcome == BattleOutcome.Averted) avertedQ++;
            if (!quartered.Wintered) continue;
            wintered++;
            Assert.Equal(HistorySeason.Spring, quartered.Season);
            Assert.True(quartered.Year == plain.Year + 1 || quartered.Year == 9);
            Assert.Contains("winter_quarters", quartered.Beats);
            // The record keeps the host that marched; the winter is in its loss share.
            Assert.Equal(plain.Attacker.Strength, quartered.Attacker.Strength, 9);
            Assert.True(quartered.Attacker.Loss >= Profile.Logistics.WinterQuartersLoss * (1 - way.ColdTolerance / 100.0) - 1e-12, "a winter in quarters costs men");
        }
        _output.WriteLine($"averted: in the field {averted}, with quarters {avertedQ}; wintered {wintered}/2000");
        Assert.True(wintered > 1000);
        Assert.True(avertedQ < averted);
    }
}
