using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Peoples;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X3 (docs/HISTORY_EXPANSION_PLAN.md): wars fought as battles behind <see cref="HistoryFeatures.Battles"/>. The decision to go
/// to war is the legacy one; every war is a battle record narrated by one event; captures and conquests follow from the outcome;
/// generals come from the court and can fall. With the flag off nothing changes (see <see cref="LegacyHistoryFixtureTests"/>).
/// </summary>
public class WarBattleTests
{
    private readonly ITestOutputHelper _output;

    public WarBattleTests(ITestOutputHelper output) => _output = output;

    private static World Build(int seed, HistoryFeatures features, int size = 3, int turns = 30)
        => new WorldBuilder().WithSize(size).WithSeed(seed).WithHistory(c => { c.Features = features; c.TurnCount = turns; }).Build();

    private static int TagInt(HistoryEvent e, string prefix)
    {
        foreach (var t in e.Tags)
            if (t.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(t.Substring(prefix.Length), out int v)) return v;
        return -1;
    }

    private static bool IsWar(HistoryEvent e) => e.Kind == HistoryEventKind.War && e.Tags.Contains("war");

    private static bool IsCampaign(HistoryEvent e) => e.Kind == HistoryEventKind.Campaign
        && (e.Tags.Contains(WarOperator.SupplyFailedTag) || e.Tags.Contains(WarOperator.DeclinedTag));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void TheFirstWars_AreTheLegacyDecisions(int seed)
    {
        using var off = Build(seed, HistoryFeatures.None);
        using var on = Build(seed, HistoryFeatures.Battles);
        var legacy = off.History.Events.Where(IsWar).ToList();
        Assert.NotEmpty(legacy);
        int first = legacy.Min(e => e.Turn);
        var expected = legacy.Where(e => e.Turn == first).Select(e => (TagInt(e, "attacker:"), TagInt(e, "defender:"))).OrderBy(x => x).ToList();
        var actual = on.History.Events.Where(e => e.Turn == first && (IsWar(e) || IsCampaign(e)))
            .Select(e => (TagInt(e, "attacker:"), TagInt(e, "defender:"))).OrderBy(x => x).ToList();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1, HistoryFeatures.Battles)]
    [InlineData(2, HistoryFeatures.Battles | HistoryFeatures.Persons)]
    [InlineData(5, HistoryFeatures.Battles | HistoryFeatures.Persons)]
    public void EveryWar_IsOneBattleRecord_AndTheRecordsAgree(int seed, HistoryFeatures features)
    {
        using var world = Build(seed, features);
        var h = world.History;
        Assert.NotEmpty(h.Battles);
        foreach (var e in h.Events.Where(e => IsWar(e) || IsCampaign(e)))
        {
            int id = TagInt(e, "battle:");
            Assert.True(id >= 0, $"event {e.Id} narrates no battle");
            var b = h.Battles[id];
            Assert.Equal(e.Id, b.EventId);
            Assert.Equal(e.Turn, b.Turn);
            Assert.Equal(TagInt(e, "attacker:"), b.Attacker.Polity);
            Assert.Equal(TagInt(e, "defender:"), b.Defender.Polity);
            Assert.Contains($"outcome:{WarOperator.OutcomeTag(b.Outcome)}", e.Tags);
            Assert.Equal(b.Outcome == BattleOutcome.Averted, IsCampaign(e));
        }
        foreach (var b in h.Battles)
        {
            Assert.InRange(b.Attacker.LossShare, 0, 1);
            Assert.InRange(b.Defender.LossShare, 0, 1);
            Assert.InRange(b.Year, 0, 9);
            Assert.True(b.Terrain >= 0);
            if (b.Outcome != BattleOutcome.Averted)
            {
                Assert.True(b.Attacker.Tactic >= 0 && b.Defender.Tactic >= 0, $"battle {b.Id} has no tactic");
                Assert.NotEmpty(b.Beats);
            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void CapturesAndConquests_FollowDecisiveBattles(int seed)
    {
        using var world = Build(seed, HistoryFeatures.Battles | HistoryFeatures.Persons);
        var h = world.History;
        bool Routed(int turn, int attacker, int defender) => h.Battles.Any(b => b.Turn == turn && b.Attacker.Polity == attacker
            && b.Defender.Polity == defender && b.Outcome == BattleOutcome.AttackerDecisive);
        foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.SettlementCaptured))
            Assert.True(Routed(e.Turn, TagInt(e, "to:"), TagInt(e, "from:")), $"capture {e.Id} without a decisive battle");
        foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.Conquest))
            Assert.True(Routed(e.Turn - 1, TagInt(e, "winner:"), TagInt(e, "loser:")), $"conquest {e.Id} without a decisive battle");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    public void Generals_ComeFromTheCourt_AndFallInBattle(int seed)
    {
        using var world = Build(seed, HistoryFeatures.Battles | HistoryFeatures.Persons);
        var h = world.History;
        int battleCause = h.Strings.TryGetId("battle", out int id) ? id : -2;
        int led = 0;
        foreach (var b in h.Battles)
        {
            foreach (var side in new[] { b.Attacker, b.Defender })
            {
                if (side.General < 0) continue;
                led++;
                var g = h.Persons[side.General];
                Assert.Equal(side.Polity, g.PolityId);
                Assert.True(g.Alive || g.DiedTurn >= b.Turn, $"general {g.Id} led at turn {b.Turn} after dying at {g.DiedTurn}");
            }
        }
        Assert.True(led > h.Battles.Count, $"only {led} commanders over {h.Battles.Count} battles");
        foreach (var p in h.Persons.Where(p => !p.Alive && p.DeathCause == battleCause))
            Assert.Contains(h.Battles, b => b.Turn == p.DiedTurn && (b.Attacker.General == p.Id || b.Defender.General == p.Id) && b.Beats.Contains(h.Strings.Intern("general_fell")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void EveryLivingPolity_HasARuler_SaveTheTurnOneFellInBattle(int seed)
    {
        using var world = Build(seed, HistoryFeatures.Battles | HistoryFeatures.Persons);
        var h = world.History;
        Assert.True(h.Strings.TryGetId("ruler", out int ruler));
        h.Strings.TryGetId("battle", out int battle);
        foreach (var polity in h.Polities)
        {
            int last = polity.Alive ? h.SimulatedTurns : polity.EndedTurn - 1;
            for (int t = polity.FoundedTurn + 1; t <= last; t++)
            {
                int holding = h.Tenures.Count(x => x.Polity == polity.Id && x.Office == ruler && x.FromTurn <= t && (x.ToTurn == -1 || x.ToTurn > t));
                bool interregnum = h.Tenures.Any(x => x.Polity == polity.Id && x.Office == ruler && x.ToTurn == t
                    && h.Persons[x.Person].DiedTurn == t && h.Persons[x.Person].DeathCause == battle);
                Assert.True(holding == 1 || (holding == 0 && interregnum), $"polity {polity.Id} has {holding} rulers at turn {t}");
            }
        }
    }

    [Fact]
    public void SameSeed_SameBytes_AndTheyRoundTrip()
    {
        using var a = Build(9, HistoryFeatures.Battles | HistoryFeatures.Persons);
        using var b = Build(9, HistoryFeatures.Battles | HistoryFeatures.Persons);
        var bytes = a.History.ToBytes();
        Assert.Equal(bytes, b.History.ToBytes());
        Assert.Equal(bytes, HistoryCatalog.Load(new MemoryStream(bytes)).ToBytes());
    }

    [Fact]
    public void Corpus_OutcomesFallInTheirBands()
    {
        var all = new List<HistoryBattle>();
        for (int seed = 1; seed <= 12; seed++)
        {
            using var world = Build(seed, HistoryFeatures.Battles | HistoryFeatures.Persons);
            var h = world.History;
            all.AddRange(h.Battles);
            _output.WriteLine($"seed {seed}: battles {h.Battles.Count}, " + string.Join(", ", h.Battles.GroupBy(b => b.Outcome).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))
                + "; kinds " + string.Join(",", h.Battles.GroupBy(b => b.Kind).Select(g => $"{g.Key}={g.Count()}"))
                + "; terrain " + string.Join(",", h.Battles.GroupBy(b => b.Terrain).Select(g => $"{h.Strings.NameOf(g.Key)}={g.Count()}"))
                + $"; generals fell {h.Persons.Count(p => !p.Alive && h.Strings.NameOf(p.DeathCause) == "battle")}"
                + $"; nemeses {h.Relations.Count(r => h.Strings.NameOf(r.Kind) == "nemesis")}");
        }
        var fought = all.Where(b => b.Outcome != BattleOutcome.Averted).ToList();
        double decisive = fought.Count(b => b.Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.DefenderDecisive) / (double)fought.Count;
        _output.WriteLine($"battles {all.Count}, averted {all.Count - fought.Count}, decisive {decisive:P1}");
        Assert.InRange(decisive, 0.15, 0.35);
    }

    [Fact]
    public void WarfareProfile_LintsEveryShippedCulture()
    {
        var store = PeoplesStore.Load();
        var profile = WarfareProfile.From(store);
        Assert.Empty(profile.UnknownKeys);
        foreach (var culture in store.Cultures.Keys)
        {
            var way = profile.WayOf(culture);
            Assert.Equal(culture, way.Key, ignoreCase: true);
            Assert.True(way.Doctrine.Sum() > 0);
            foreach (var u in way.Units) Assert.Contains(profile.Tactics, t => string.Equals(t.Arm, u.Arm, StringComparison.OrdinalIgnoreCase));
        }
    }
}
