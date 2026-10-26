using System.Security.Cryptography;
using RoguelikeToolkit.World.Core.History;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X5 (docs/HISTORY_EXPANSION_PLAN.md §4): the logistics network behind <see cref="HistoryFeatures.Logistics"/>. With the flag off,
/// siege worlds are byte-identical to X4; with it on, route safety stays in 0..1 and is sampled every turn, forts and waystations
/// make roads safer, waystations stand on long roads, and hosts short of winter supply winter in quarters.
/// </summary>
public class LogisticsWorldTests
{
    private const HistoryFeatures X4 = HistoryFeatures.Persons | HistoryFeatures.Battles | HistoryFeatures.Sieges;
    private const HistoryFeatures All = X4 | HistoryFeatures.Logistics;
    private readonly ITestOutputHelper _output;

    public LogisticsWorldTests(ITestOutputHelper output) => _output = output;

    private static World Build(int seed, HistoryFeatures features = All, int size = 3, int turns = 30)
        => new WorldBuilder().WithSize(size).WithSeed(seed).WithHistory(c => { c.Features = features; c.TurnCount = turns; }).Build();

    /// <summary>Catalog byte hashes of Persons + Battles + Sieges worlds captured at X4 (ba56616).</summary>
    [Theory]
    [InlineData(1, 3, "35C93995CB4E59A42152D8F5F241B431FFEE99DB5C93EDF380C2D8CBF4E328C8")]
    [InlineData(2, 3, "ED345C456838402E815C6F858787DFB902ED6F5D2DD790331DB3FB7D2626613F")]
    [InlineData(3, 4, "0CB038925379EAFF84DC8F044F4654FC7FD253A10FDA2B1B1DF7C2E6ED641B55")]
    [InlineData(5, 3, "C495A51EFAC72581BB8F6989D07540A5D1B58B25DC074023FF40329BF164754D")]
    public void LogisticsOff_SiegeWorldsAreTheX4Bytes(int seed, int size, string hash)
    {
        using var world = Build(seed, X4, size);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(world.History.ToBytes())));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(9)]
    public void RouteSafety_StaysInRange_AndIsSampledEveryTurn(int seed)
    {
        using var world = Build(seed);
        var h = world.History;
        Assert.NotEmpty(h.RouteStates);
        foreach (var r in h.RouteStates)
        {
            Assert.InRange(r.Safety, 0, 1);
            Assert.True(r.Forts >= 0 && r.Waystations >= 0);
            Assert.Contains(h.Roads, x => x.RouteA == r.RouteA && x.RouteB == r.RouteB);
        }
        foreach (var s in h.RouteSamples) Assert.InRange(s.Safety, 0, 1);
        // The last turn's samples are the final states.
        int last = h.SimulatedTurns;
        var final = h.RouteSamples.Where(s => s.Turn == last).ToList();
        Assert.Equal(h.RouteStates.Count, final.Count);
        for (int i = 0; i < final.Count; i++)
            Assert.Equal((h.RouteStates[i].RouteA, h.RouteStates[i].RouteB, h.RouteStates[i].Safety), (final[i].RouteA, final[i].RouteB, final[i].Safety));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void Waystations_StandOnLongRoads(int seed)
    {
        using var world = Build(seed);
        var h = world.History;
        var vectors = world.Map.DataStore.GetTileVectors().ToArray();
        double reach = LogisticsProfileData().WaystationDegrees;
        var founded = h.Events.Where(e => e.Kind == HistoryEventKind.SettlementFounded && e.Tags.Contains($"why:{LogisticsOperator.WaystationWhy}")).ToList();
        foreach (var e in founded)
        {
            Assert.Contains("role:Waystation", e.Tags);
            int index = int.Parse(e.Tags.First(t => t.StartsWith("settlement:", StringComparison.Ordinal)).Substring("settlement:".Length));
            var s = h.Settlements[index];
            Assert.Equal(SettlementRole.Waystation, s.Role);
            Assert.Contains(h.Roads, r => r.TileA == s.TileIndex || r.TileB == s.TileIndex
                || LogisticsGraph.Degrees(vectors[r.TileA], vectors[s.TileIndex]) <= reach);
        }
        _output.WriteLine($"waystations: {founded.Count}");
    }

    private static Peoples.LogisticsDef LogisticsProfileData() => Peoples.PeoplesStore.Load().Logistics["default"];

    [Fact]
    public void Corpus_GuardedRoadsAreSafer_AndHostsWinterInQuarters()
    {
        int guarded = 0, unguarded = 0, stationed = 0, wintered = 0, fields = 0, averted = 0, decisive = 0, sieges = 0;
        double guardedSafety = 0, unguardedSafety = 0, stationedSafety = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            using var world = Build(seed);
            var h = world.History;
            foreach (var r in h.RouteStates)
            {
                if (r.Forts > 0) { guarded++; guardedSafety += r.Safety; }
                else { unguarded++; unguardedSafety += r.Safety; }
                if (r.Waystations > 0) { stationed++; stationedSafety += r.Safety; }
            }
            foreach (var b in h.Battles)
            {
                if (b.Kind == BattleKind.Siege) sieges++;
                if (b.Kind is not (BattleKind.Field or BattleKind.Ambush) || b.Parent >= 0) continue;
                fields++;
                if (b.Outcome == BattleOutcome.Averted) averted++;
                else if (b.Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.DefenderDecisive) decisive++;
                if (b.EventId < 0 || !h.Events[b.EventId].Tags.Contains(WarOperator.WinterQuartersTag)) continue;
                wintered++;
                Assert.Equal(HistorySeason.Spring, b.Season);
                Assert.Contains("winter_quarters", b.Beats.Select(h.Strings.NameOf));
            }
        }
        double g = guardedSafety / Math.Max(1, guarded), u = unguardedSafety / Math.Max(1, unguarded), w = stationedSafety / Math.Max(1, stationed);
        _output.WriteLine($"fields {fields}: averted {averted / (double)fields:P1}, decisive {decisive / (double)(fields - averted):P1} of fought, sieges {sieges / (double)fields:P1}, wintered {wintered}");
        _output.WriteLine($"routes: guarded {guarded} at {g:F2}, unguarded {unguarded} at {u:F2}, with waystations {stationed} at {w:F2}");
        Assert.True(guarded > 0 && g > u, "forts should make roads safer");
        Assert.True(wintered > 0, "some hosts should winter in quarters");
        // The network keeps the X3/X4 calibration: §11 bands.
        Assert.InRange(averted / (double)fields, 0.15, 0.40);
        Assert.InRange(decisive / (double)(fields - averted), 0.15, 0.35);
        Assert.InRange(sieges / (double)fields, 0.20, 0.40);
    }

    [Fact]
    public void LogisticsAlone_RunsWithoutBattles()
    {
        using var world = Build(2, HistoryFeatures.Logistics);
        var h = world.History;
        Assert.Empty(h.Battles);
        Assert.All(h.RouteStates, r => Assert.InRange(r.Safety, 0, 1));
        Assert.NotEmpty(h.RouteStates);
    }

    [Fact]
    public void SameSeed_SameBytes_AndTheyRoundTrip()
    {
        using var a = Build(7);
        using var b = Build(7);
        var bytes = a.History.ToBytes();
        Assert.Equal(bytes, b.History.ToBytes());
        Assert.Equal(bytes, HistoryCatalog.Load(new MemoryStream(bytes)).ToBytes());
    }
}
