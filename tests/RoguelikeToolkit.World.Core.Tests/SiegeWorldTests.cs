using System.Security.Cryptography;
using RoguelikeToolkit.World.Core.History;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X4 (docs/HISTORY_EXPANSION_PLAN.md): sieges, relief battles and forts behind <see cref="HistoryFeatures.Sieges"/>. With the
/// flag off, battle worlds are byte-identical to X3; with it on, every siege follows a field battle, every capture and conquest
/// follows a decisive battle or a fall, forts are built by the peoples the data says build them, and they make roads safer.
/// </summary>
public class SiegeWorldTests
{
    private const HistoryFeatures All = HistoryFeatures.Persons | HistoryFeatures.Battles | HistoryFeatures.Sieges;
    private readonly ITestOutputHelper _output;

    public SiegeWorldTests(ITestOutputHelper output) => _output = output;

    private static World Build(int seed, HistoryFeatures features = All, int size = 3, int turns = 30)
        => new WorldBuilder().WithSize(size).WithSeed(seed).WithHistory(c => { c.Features = features; c.TurnCount = turns; }).Build();

    private static int TagInt(HistoryEvent e, string prefix)
    {
        foreach (var t in e.Tags)
            if (t.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(t.Substring(prefix.Length), out int v)) return v;
        return -1;
    }

    /// <summary>Catalog byte hashes of Persons + Battles worlds captured at X3 (87d22ae).</summary>
    [Theory]
    [InlineData(1, 3, "EDDB4A5F5349EC795A26A8FE8869B1DCEE836D4091906C52B280A2FD36854FDF")]
    [InlineData(2, 3, "FFE1C5F0DAEE86B79A7DF85102C1F430C3BD16C4BF3E6A113F7AF5318DF52E2C")]
    [InlineData(3, 4, "42D63E9ADD5A45E4AEEDEFBBDDA479F2AE66EB928D09E510E4C4A310B3DB13A2")]
    [InlineData(5, 3, "0A2CD9A148D0B9D8719665DC7524DF25D2BE75773DDFD80AD24F0090F194C490")]
    public void SiegesOff_BattleWorldsAreTheX3Bytes(int seed, int size, string hash)
    {
        using var world = Build(seed, HistoryFeatures.Persons | HistoryFeatures.Battles, size);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(world.History.ToBytes())));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public void EverySiege_FollowsItsFieldBattle_AndEveryReliefItsSiege(int seed)
    {
        using var world = Build(seed);
        var h = world.History;
        Assert.Contains(h.Battles, b => b.Kind == BattleKind.Siege);
        foreach (var b in h.Battles.Where(b => b.Kind == BattleKind.Siege))
        {
            Assert.InRange(b.SiegeMonths, 1, new HistoryContext().MaxSiegeMonths);
            Assert.True(b.SiegeEnd >= 0);
            Assert.True(b.Settlement >= 0 && h.Settlements[b.Settlement].FoundedTurn <= b.Turn);
            var parent = h.Battles[b.Parent];
            Assert.True(parent.Kind is BattleKind.Field or BattleKind.Ambush, $"siege {b.Id} follows a {parent.Kind}");
            Assert.Equal((parent.Turn, parent.Attacker.Polity, parent.Defender.Polity), (b.Turn, b.Attacker.Polity, b.Defender.Polity));
            Assert.True(parent.Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.AttackerVictory);
            Assert.InRange(b.Attacker.LossShare, 0, 1);
            Assert.InRange(b.Defender.LossShare, 0, 1);
            if (b.EventId >= 0)
            {
                var e = h.Events[b.EventId];
                Assert.Equal(HistoryEventKind.Siege, e.Kind);
                Assert.Contains($"battle:{b.Id}", e.Tags);
                Assert.Contains($"end:{h.Strings.NameOf(b.SiegeEnd)}", e.Tags);
            }
        }
        foreach (var b in h.Battles.Where(b => b.Parent >= 0 && h.Battles[b.Parent].Kind == BattleKind.Siege))
        {
            var siege = h.Battles[b.Parent];
            Assert.True(b.Kind is BattleKind.Relief or BattleKind.Ambush);
            Assert.Equal((siege.Defender.Polity, siege.Attacker.Polity), (b.Attacker.Polity, b.Defender.Polity));
            if (h.Strings.NameOf(siege.SiegeEnd) == SiegeResolver.Relieved) Assert.True(b.Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.AttackerVictory or BattleOutcome.AttackerPyrrhic);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void CapturesAndConquests_FollowRoutsOrFalls(int seed)
    {
        using var world = Build(seed);
        var h = world.History;
        bool Fell(HistoryBattle b) => b.Kind == BattleKind.Siege && SiegeResolver.Fell(h.Strings.NameOf(b.SiegeEnd));
        foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.SettlementCaptured))
        {
            int settlement = TagInt(e, "settlement:"), captor = TagInt(e, "to:"), loser = TagInt(e, "from:");
            Assert.True(h.Battles.Any(b => b.Turn == e.Turn && b.Attacker.Polity == captor && b.Defender.Polity == loser
                && ((b.Kind is BattleKind.Field or BattleKind.Ambush && b.Outcome == BattleOutcome.AttackerDecisive) || (Fell(b) && b.Settlement == settlement))),
                $"capture {e.Id} follows neither a rout nor a fall");
        }
        foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.Conquest))
        {
            int winner = TagInt(e, "winner:"), loser = TagInt(e, "loser:");
            Assert.True(h.Battles.Any(b => b.Turn == e.Turn - 1 && b.Attacker.Polity == winner && b.Defender.Polity == loser
                && ((b.Kind is BattleKind.Field or BattleKind.Ambush && b.Outcome == BattleOutcome.AttackerDecisive)
                    || (Fell(b) && h.Settlements[b.Settlement].Role == SettlementRole.Capital))),
                $"conquest {e.Id} follows neither a rout nor a fallen capital");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void EveryLivingPolity_HasARuler_SaveTheTurnOneFell(int seed)
    {
        using var world = Build(seed);
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
    public void Corpus_SiegesForts_AndRoads()
    {
        int wars = 0, sieges = 0, guarded = 0;
        var forts = new Dictionary<string, int>(StringComparer.Ordinal);
        var polityTurns = new Dictionary<string, int>(StringComparer.Ordinal);
        var ends = new Dictionary<string, int>(StringComparer.Ordinal);
        double guardedSafety = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            using var world = Build(seed);
            var h = world.History;
            wars += h.Battles.Count(b => b.Kind is BattleKind.Field or BattleKind.Ambush && b.Parent < 0);
            foreach (var b in h.Battles.Where(b => b.Kind == BattleKind.Siege))
            {
                sieges++;
                string end = h.Strings.NameOf(b.SiegeEnd);
                ends[end] = ends.GetValueOrDefault(end) + 1;
            }
            foreach (var p in h.Polities)
            {
                int alive = (p.Alive ? h.SimulatedTurns : p.EndedTurn) - p.FoundedTurn;
                polityTurns[p.CultureKey] = polityTurns.GetValueOrDefault(p.CultureKey) + Math.Max(0, alive);
            }
            foreach (var e in h.Events.Where(e => e.Kind == HistoryEventKind.SettlementFounded && e.Tags.Contains($"why:{FortificationOperator.FortWhy}")))
            {
                Assert.Contains("role:Post", e.Tags);
                string culture = h.Polities[e.PolityId].CultureKey;
                forts[culture] = forts.GetValueOrDefault(culture) + 1;
            }
            foreach (var r in h.RouteStates)
            {
                Assert.InRange(r.Safety, 0, 1);
                if (r.Forts > 0) { guarded++; guardedSafety += r.Safety; }
            }
        }
        double rate(string culture) => forts.GetValueOrDefault(culture) / (double)Math.Max(1, polityTurns.GetValueOrDefault(culture));
        _output.WriteLine($"wars {wars}, sieges {sieges} ({sieges / (double)wars:P1}); ends " + string.Join(", ", ends.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
        _output.WriteLine("forts per 100 polity-turns: " + string.Join(", ", polityTurns.OrderBy(k => k.Key).Select(k => $"{k.Key}={100 * rate(k.Key):F2}")));
        _output.WriteLine($"guarded routes {guarded}, mean safety {(guarded > 0 ? guardedSafety / guarded : 0):F2}");
        Assert.InRange(sieges / (double)wars, 0.20, 0.40);
        Assert.True(rate("dwarven_clan") > rate("human_common"), "dwarves should fortify more than humans");
        Assert.True(guarded > 0 && guardedSafety / guarded > HistoryRouteState.NewRouteSafety, "forts should make roads safer");
        Assert.True(ends.Count >= 4, "sieges should end in several ways");
    }

    [Fact]
    public void SameSeed_SameBytes_AndTheyRoundTrip()
    {
        using var a = Build(11);
        using var b = Build(11);
        var bytes = a.History.ToBytes();
        Assert.Equal(bytes, b.History.ToBytes());
        Assert.Equal(bytes, HistoryCatalog.Load(new MemoryStream(bytes)).ToBytes());
    }
}
