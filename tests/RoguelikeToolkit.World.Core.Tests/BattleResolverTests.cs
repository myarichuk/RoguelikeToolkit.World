using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X3 (docs/HISTORY_EXPANSION_PLAN.md §11): the battle model's statistics over 10k synthetic battle keys. Every comparison
/// shares the keys between its two arms, so the difference measures the term under test, not noise.
/// </summary>
public class BattleResolverTests
{
    private const int Keys = 10_000;
    private readonly ITestOutputHelper _output;

    public BattleResolverTests(ITestOutputHelper output) => _output = output;

    private static readonly PeoplesStore Store = PeoplesStore.Load();
    private static readonly WarfareProfile Profile = WarfareProfile.From(Store);

    private static Func<string, Rng> Draws(int key) => purpose => HistoryRng.Create(1, "battle-test", 0, key, purpose);

    private static WarfareProfile.Way Way(string culture = "default") => Profile.WayOf(culture);

    /// <summary>A way of war that fights only <paramref name="tactic"/>.</summary>
    private static WarfareProfile.Way Only(string tactic, WarfareProfile.Way? basis = null)
    {
        var b = basis ?? Way();
        var doctrine = new double[Profile.Tactics.Length];
        doctrine[Profile.TacticIndex(tactic)] = 1;
        return b with { Doctrine = doctrine };
    }

    private static WarfareProfile.Way InSeason(HistorySeason season, WarfareProfile.Way? basis = null)
    {
        var seasons = new double[4];
        seasons[(int)season] = 1;
        return (basis ?? Way()) with { CampaignSeasons = seasons };
    }

    private static BattleResolver.Field Open(bool river = false, double range = 10)
        => new(0, Profile.TerrainIndex("open"), river, 0.5, range, 0);

    private static BattleResolver.Field Ground(string terrain) => new(0, Profile.TerrainIndex(terrain), false, 0.5, 10, 0);

    private static BattleResolver.Side Side(int polity, double levy = 1000, WarfareProfile.Way? way = null, BattleResolver.Commander? general = null)
        => new(polity, levy, way ?? Way(), general);

    private static IEnumerable<BattleResolver.Result> Run(BattleResolver.Side a, BattleResolver.Side d, params BattleResolver.Field[] fields)
    {
        for (int k = 0; k < Keys; k++) yield return BattleResolver.Resolve(Profile, a, d, fields, Draws(k), 10);
    }

    private static double WinRate(BattleResolver.Side a, BattleResolver.Side d, params BattleResolver.Field[] fields)
        => Run(a, d, fields).Count(r => r.AttackerWon) / (double)Keys;

    [Fact]
    public void ShippedBattleData_LoadsAndNamesOnlyKnownThings()
    {
        Assert.Empty(Profile.UnknownKeys);
        Assert.Equal(10, Profile.Tactics.Length);
        foreach (var culture in Store.Cultures.Keys)
            Assert.True(Store.Warfare.ContainsKey(culture), $"culture {culture} has no Warfare file");
        Assert.True(Store.Warfare.ContainsKey("default"));
    }

    [Fact]
    public void EqualForces_FavourTheDefenderSlightly()
    {
        double rate = WinRate(Side(0), Side(1), Open());
        _output.WriteLine($"equal forces: attacker wins {rate:P1}");
        Assert.InRange(rate, 0.38, 0.50);
    }

    [Fact]
    public void TwoToOne_WinsThreeTimesInFour()
    {
        double rate = WinRate(Side(0, 2000), Side(1), Open());
        _output.WriteLine($"2:1: attacker wins {rate:P1}");
        Assert.True(rate >= 0.75, $"2:1 won only {rate:P1}");
    }

    [Fact]
    public void RiverCrossing_CostsTheAttackerTenPoints()
    {
        double dry = WinRate(Side(0), Side(1), Open());
        double wet = WinRate(Side(0), Side(1), Open(river: true));
        _output.WriteLine($"dry {dry:P1}, river {wet:P1}");
        Assert.True(dry - wet >= 0.08, $"a river cost only {dry - wet:P1}");
    }

    [Fact]
    public void Cavalry_IsWorthMoreInTheOpenThanInTheWoods()
    {
        var horse = Side(0, way: Only("heavy_charge"));
        double open = WinRate(horse, Side(1), Ground("open"));
        double forest = WinRate(horse, Side(1), Ground("forest"));
        _output.WriteLine($"heavy charge: open {open:P1}, forest {forest:P1}");
        Assert.True(open - forest >= 0.15, $"gap only {open - forest:P1}");
    }

    [Fact]
    public void Winter_RaisesAttrition()
    {
        var summer = Run(Side(0, way: InSeason(HistorySeason.Summer)), Side(1), Open(range: 20)).ToList();
        var winter = Run(Side(0, way: InSeason(HistorySeason.Winter)), Side(1), Open(range: 20)).ToList();
        Assert.All(winter, r => Assert.Equal(HistorySeason.Winter, r.Season));
        double s = summer.Average(r => r.Attacker.Loss), w = winter.Average(r => r.Attacker.Loss);
        _output.WriteLine($"attacker loss: summer {s:P1}, winter {w:P1}; wins summer {summer.Count(r => r.AttackerWon) / (double)Keys:P1}, winter {winter.Count(r => r.AttackerWon) / (double)Keys:P1}");
        Assert.True(w > s + 0.03, $"winter loss {w:P1} vs summer {s:P1}");
    }

    [Fact]
    public void MartialThirtyPoints_IsWorthEightPoints()
    {
        var plain = new BattleResolver.Commander(-1, 50, 50, 0, Array.Empty<string>());
        var gifted = plain with { Martial = 80 };
        double a = WinRate(Side(0, general: plain), Side(1, general: plain), Open());
        double b = WinRate(Side(0, general: gifted), Side(1, general: plain), Open());
        _output.WriteLine($"martial 50 {a:P1}, martial 80 {b:P1}");
        Assert.True(b - a >= 0.08, $"martial +30 gave only {b - a:P1}");
    }

    [Fact]
    public void WinChance_IsMonotoneInStrength()
    {
        var ratios = new[] { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 5.0 };
        var wins = new bool[ratios.Length][];
        for (int i = 0; i < ratios.Length; i++) wins[i] = Run(Side(0, 1000 * ratios[i]), Side(1), Open()).Select(r => r.AttackerWon).ToArray();
        _output.WriteLine(string.Join(", ", ratios.Select((r, i) => $"{r}:{wins[i].Count(w => w) / (double)Keys:P1}")));
        // Shared keys: a stronger attacker never turns a won battle into a lost one.
        for (int i = 1; i < ratios.Length; i++)
            for (int k = 0; k < Keys; k++)
                Assert.True(!wins[i - 1][k] || wins[i][k], $"key {k} lost at ratio {ratios[i]} but won at {ratios[i - 1]}");
    }

    [Fact]
    public void Outcomes_AreBandedLossesBoundedAndBeatsKnown()
    {
        var beats = new HashSet<string>(Profile.Beats.Select(b => b.Key));
        var results = Run(Side(0, 1200, Way("orcish_horde")), Side(1, 1000, Way("dwarven_clan")), Open(), Ground("hills"), Ground("forest"), Open(river: true)).ToList();
        foreach (var r in results)
        {
            Assert.InRange(r.Attacker.Loss, 0, 1);
            Assert.InRange(r.Defender.Loss, 0, 1);
            Assert.All(r.Beats, b => Assert.Contains(b, beats));
            if (r.Outcome != BattleOutcome.Averted) Assert.Single(r.Beats, b => !Profile.Beats.First(x => x.Key == b).Always);
        }
        var byOutcome = results.GroupBy(r => r.Outcome).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count() / (double)Keys:P1}");
        _output.WriteLine(string.Join(", ", byOutcome));
        _output.WriteLine(string.Join(", ", results.SelectMany(r => r.Beats).GroupBy(b => b).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}")));
        Assert.Contains(results, r => r.Kind == BattleKind.Ambush);
        Assert.Contains(results, r => r.RiverBypassed);
    }

    [Fact]
    public void FarFromHome_TheCampaignIsAverted()
    {
        var far = new BattleResolver.Field(0, Profile.TerrainIndex("desert"), false, 0, 10, 60);
        var results = Run(Side(0), Side(1), far).ToList();
        Assert.All(results, r => Assert.Equal(BattleOutcome.Averted, r.Outcome));
        Assert.All(results, r => Assert.True(r.Attacker.Supply < Profile.Data.AvertedBelow));
    }

    [Fact]
    public void SameKey_SameBattle()
    {
        var a = Side(0, 1100, Way("human_common"));
        var d = Side(1, 900, Way("elven_wood"));
        var fields = new[] { Open(), Ground("forest"), Ground("hills") };
        for (int k = 0; k < 200; k++)
            Assert.Equal(BattleResolver.Resolve(Profile, a, d, fields, Draws(k), 10).ToString(), BattleResolver.Resolve(Profile, a, d, fields, Draws(k), 10).ToString());
    }
}
