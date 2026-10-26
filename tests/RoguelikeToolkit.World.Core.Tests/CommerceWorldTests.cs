using System.Security.Cryptography;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X6 (docs/HISTORY_EXPANSION_PLAN.md §4–§5): commerce, merchant princes, outlaw bands and intrigue behind
/// <see cref="HistoryFeatures.Commerce"/>, <see cref="HistoryFeatures.Outlaws"/> and <see cref="HistoryFeatures.Intrigue"/>. With the
/// three flags off, logistics worlds are byte-identical to X5; with them on, the corpus stays in the plan's bands (§11), records
/// agree with each other, and an exposed sponsorship leads to a war in at least one world of 25.
/// </summary>
public class CommerceWorldTests
{
    private const HistoryFeatures X5 = HistoryFeatures.Persons | HistoryFeatures.Battles | HistoryFeatures.Sieges | HistoryFeatures.Logistics;
    private readonly ITestOutputHelper _output;

    public CommerceWorldTests(ITestOutputHelper output) => _output = output;

    private static World Build(int seed, HistoryFeatures features = HistoryFeatures.All, int size = 3, int turns = 30)
        => new WorldBuilder().WithSize(size).WithSeed(seed).WithHistory(c => { c.Features = features; c.TurnCount = turns; }).Build();

    private static int TagInt(HistoryEvent e, string prefix)
    {
        foreach (var t in e.Tags)
            if (t.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(t.Substring(prefix.Length), out int v)) return v;
        return -1;
    }

    /// <summary>Catalog byte hashes of Persons + Battles + Sieges + Logistics worlds captured at X5 (ccdf850).</summary>
    [Theory]
    [InlineData(1, 3, "F5A50DA4C160E35C25D0A05040AC9EF2EED01E45A1A47A1FBF7729CCEBF5397F")]
    [InlineData(2, 3, "25C3CBF847117CF29FC00D79BBB289AC377A91FA97BAC288DE23315ADDBF2038")]
    [InlineData(3, 4, "CE1C24CE95205D19F65B57582A90FA84C26F79C2521DC378C1E7F1E6A68256B4")]
    [InlineData(6, 3, "08461FA345FB7E00575C6DB1071AB6F9CF3B02BC0BF8B9978934C7FBEFE50EFD")]
    public void X6Off_LogisticsWorldsAreTheX5Bytes(int seed, int size, string hash)
    {
        using var world = Build(seed, X5, size);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(world.History.ToBytes())));
    }

    [Fact]
    public void ShippedData_LoadsAndNamesOnlyKnownThings()
    {
        var store = PeoplesStore.Load();
        Assert.True(store.Commerce.ContainsKey("default") && store.Outlaws.ContainsKey("default") && store.Intrigues.ContainsKey("default"));
        var profile = TradeProfile.From(store);
        Assert.Empty(profile.UnknownKeys);
        Assert.Empty(profile.Warfare.UnknownKeys);
        Assert.Contains(true, profile.Haven);
        Assert.NotEmpty(profile.Plots);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(14)]
    public void Records_Agree(int seed)
    {
        using var world = Build(seed);
        var h = world.History;
        string[] plots = { "sponsorship", "sponsorship_exposed", "spy_network", "sabotage", "bribe", "assassination", "theft", "discovered" };
        string[] covert = { "sabotage", "bribe", "assassination", "theft", "discovered" };
        foreach (var p in h.Persons)
        {
            Assert.True(p.Wealth >= 0 && p.Pledged >= 0);
            Assert.InRange(p.Loyalty, -100, 100);
            if (p.PolityId < 0) Assert.Equal(PersonKind.Outlaw, p.Kind);
            if (p.Kind == PersonKind.Agent)
            {
                Assert.True(p.PolityId >= 0 && p.SeatSettlement >= 0, $"agent {p.Id} serves a sponsor from a seat");
                Assert.NotEqual(p.PolityId, h.Settlements[p.SeatSettlement].PolityId == p.PolityId && p.Alive ? p.PolityId : -2);
            }
        }
        foreach (var r in h.RouteStates)
        {
            Assert.InRange(r.Traffic, 0, 1);
            if (r.OutlawBand >= 0)
            {
                var band = h.Persons[r.OutlawBand];
                Assert.Equal(PersonKind.Outlaw, band.Kind);
                Assert.True(band.Alive, $"route {r.RouteA}-{r.RouteB} names dead band {band.Id}");
            }
            if (r.Sponsor >= 0) Assert.True(r.OutlawBand >= 0, "a sponsor pays a band");
        }
        foreach (var e in h.Events)
        {
            switch (e.Kind)
            {
                case HistoryEventKind.Intrigue:
                {
                    string plot = e.Tags.First(t => t.StartsWith("plot:", StringComparison.Ordinal)).Substring(5);
                    Assert.Contains(plot, plots);
                    Assert.True(TagInt(e, "actor:") >= 0, $"intrigue {e.Id} names no sponsor");
                    if (!covert.Contains(plot)) break;
                    // Every covert act has an operative: an agent, or a sponsored band.
                    int agent = TagInt(e, "agent:"), band = TagInt(e, "band:");
                    Assert.True(agent >= 0 || band >= 0, $"{plot} {e.Id} has no operative");
                    if (agent >= 0)
                    {
                        Assert.Equal(PersonKind.Agent, h.Persons[agent].Kind);
                        Assert.Equal(TagInt(e, "actor:"), h.Persons[agent].PolityId);
                        if (e.Tags.Contains("caught"))
                            Assert.Equal((e.Turn, IntrigueOperator.ExecutedCause), (h.Persons[agent].DiedTurn, h.Strings.NameOf(h.Persons[agent].DeathCause)));
                    }
                    if (band >= 0) Assert.Equal(PersonKind.Outlaw, h.Persons[band].Kind);
                    break;
                }
                case HistoryEventKind.CaravanRaided:
                case HistoryEventKind.OutlawRose:
                case HistoryEventKind.OutlawEnded:
                {
                    var route = e.Tags.FirstOrDefault(t => t.StartsWith("route:", StringComparison.Ordinal));
                    if (route == null) break;
                    var ends = route.Substring(6).Split('-').Select(int.Parse).ToArray();
                    Assert.Contains(h.Roads, x => x.RouteA == ends[0] && x.RouteB == ends[1]);
                    int person = TagInt(e, e.Kind == HistoryEventKind.CaravanRaided ? "band:" : "person:");
                    if (person >= 0) Assert.Equal(PersonKind.Outlaw, h.Persons[person].Kind);
                    break;
                }
            }
            if (e.Kind == HistoryEventKind.OutlawEnded && e.Tags.Contains("end:hanged"))
                Assert.Equal(OutlawOperator.HangedCause, h.Strings.NameOf(h.Persons[TagInt(e, "person:")].DeathCause));
        }
    }

    [Fact]
    public void Corpus_StaysInTheBands_AndExposedSponsorsAreMadeToPay()
    {
        int worlds = 25, bands = 0, raids = 0, routeTurns = 0, warWorlds = 0, fields = 0, averted = 0, decisive = 0, sieges = 0;
        double living = 0;
        int polities = 0;
        var plots = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int seed = 1; seed <= worlds; seed++)
        {
            using var world = Build(seed);
            var h = world.History;
            routeTurns += h.RouteSamples.Count;
            bool war = false;
            foreach (var e in h.Events)
            {
                if (e.Kind == HistoryEventKind.OutlawRose) bands++;
                if (e.Kind == HistoryEventKind.CaravanRaided) raids++;
                if (e.Kind != HistoryEventKind.Intrigue) continue;
                string plot = e.Tags.First(t => t.StartsWith("plot:", StringComparison.Ordinal)).Substring(5);
                plots[plot] = plots.GetValueOrDefault(plot) + 1;
                if (plot != "sponsorship_exposed") continue;
                int victim = TagInt(e, "target:"), sponsor = TagInt(e, "actor:");
                war |= h.Battles.Any(b => b.Turn > e.Turn && b.Attacker.Polity == victim && b.Defender.Polity == sponsor);
            }
            if (war) warWorlds++;
            foreach (var b in h.Battles)
            {
                if (b.Kind == BattleKind.Siege) sieges++;
                if (b.Kind is not (BattleKind.Field or BattleKind.Ambush) || b.Parent >= 0) continue;
                fields++;
                if (b.Outcome == BattleOutcome.Averted) averted++;
                else if (b.Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.DefenderDecisive) decisive++;
            }
            foreach (var p in h.Polities.Where(p => p.Alive))
            {
                living += h.Persons.Count(x => x.Alive && x.PolityId == p.Id);
                polities++;
            }
        }
        double perPolity = living / polities, bandsPerWorld = bands / (double)worlds, raidShare = raids / (double)routeTurns;
        _output.WriteLine($"living per polity {perPolity:F2}; bands per world {bandsPerWorld:F1}; raids {raidShare:P1} of route-turns; exposed-then-war worlds {warWorlds}/{worlds}");
        _output.WriteLine($"fields {fields}: averted {averted / (double)fields:P1}, decisive {decisive / (double)(fields - averted):P1} of fought, sieges {sieges / (double)fields:P1}");
        _output.WriteLine("plots: " + string.Join(", ", plots.Select(k => $"{k.Key}={k.Value}")));
        Assert.InRange(perPolity, 3, 7);
        Assert.InRange(bandsPerWorld, 2, 10);
        Assert.True(raidShare < 0.30);
        Assert.True(warWorlds >= 1, "an exposed sponsorship should lead to a war in at least one world of 25");
        Assert.InRange(averted / (double)fields, 0.15, 0.40);
        Assert.InRange(decisive / (double)(fields - averted), 0.15, 0.35);
        Assert.InRange(sieges / (double)fields, 0.20, 0.40);
    }

    [Theory]
    [InlineData(HistoryFeatures.Commerce)]
    [InlineData(HistoryFeatures.Outlaws)]
    [InlineData(HistoryFeatures.Intrigue)]
    [InlineData(HistoryFeatures.Persons | HistoryFeatures.Commerce | HistoryFeatures.Outlaws)]
    public void EachFlag_RunsOnItsOwn(HistoryFeatures features)
    {
        using var a = Build(4, features);
        using var b = Build(4, features);
        Assert.Equal(a.History.ToBytes(), b.History.ToBytes());
    }

    [Fact]
    public void SameSeed_SameBytes_AndTheyRoundTrip()
    {
        using var a = Build(12);
        using var b = Build(12);
        var bytes = a.History.ToBytes();
        Assert.Equal(bytes, b.History.ToBytes());
        Assert.Equal(bytes, HistoryCatalog.Load(new MemoryStream(bytes)).ToBytes());
    }

    [Fact]
    public void ABoughtGeneral_LosesBattles()
    {
        var profile = WarfareProfile.From(PeoplesStore.Load());
        var way = profile.WayOf("human_common");
        var field = new BattleResolver.Field(0, profile.TerrainIndex("open"), false, 0.5, 10, 0);
        int loyal = 0, bought = 0;
        for (int k = 0; k < 4000; k++)
        {
            Func<string, Rng> draws = purpose => HistoryRng.Create(9, "betrayal-test", 0, k, purpose);
            var general = new BattleResolver.Commander(7, 50, 50, 0, Array.Empty<string>());
            var faithful = BattleResolver.Resolve(profile, new BattleResolver.Side(0, 1000, way, general), new BattleResolver.Side(1, 1000, way), new[] { field }, draws, 10);
            var turncoat = BattleResolver.Resolve(profile, new BattleResolver.Side(0, 1000, way, general with { Loyalty = -60 }), new BattleResolver.Side(1, 1000, way), new[] { field }, draws, 10);
            if (faithful.AttackerWon) loyal++;
            if (turncoat.AttackerWon) bought++;
            Assert.DoesNotContain("turncoat", faithful.Beats);
            if (turncoat.Outcome != BattleOutcome.Averted && !turncoat.Declined) Assert.Contains("turncoat", turncoat.Beats);
        }
        _output.WriteLine($"attacker wins: loyal general {loyal / 4000.0:P1}, bought general {bought / 4000.0:P1}");
        Assert.True(bought < loyal - 200);
    }
}
