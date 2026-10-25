using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X4 (docs/HISTORY_EXPANSION_PLAN.md §3.1, §11): the siege model and the garrisons' part in field battles, over shared synthetic
/// keys. Every comparison shares its keys between its two arms, so the difference measures the term under test.
/// </summary>
public class SiegeResolverTests
{
    private const int Keys = 4000;
    private readonly ITestOutputHelper _output;

    public SiegeResolverTests(ITestOutputHelper output) => _output = output;

    private static readonly PeoplesStore Store = PeoplesStore.Load();
    private static readonly WarfareProfile Profile = WarfareProfile.From(Store);

    private static Func<string, Rng> Draws(int key) => purpose => HistoryRng.Create(3, "siege-test", 0, key, purpose);

    private static BattleResolver.Side Side(int polity, string culture = "default", double levy = 1000, double spy = 50)
        => new(polity, levy, Profile.WayOf(culture), null, spy);

    private static SiegeResolver.Place Town(double garrison = 100, double walls = 1, bool city = false, bool resupply = false,
        HistorySeason season = HistorySeason.Summer)
        => new(0, garrison, walls, city, resupply, false, Profile.TerrainIndex("open"), season);

    private static List<SiegeResolver.Result> Run(SiegeResolver.Place place, double host = 400, double supply = 0.8, string attacker = "default",
        SiegeResolver.Relief relief = default, double spy = 50)
    {
        var list = new List<SiegeResolver.Result>(Keys);
        for (int k = 0; k < Keys; k++)
            list.Add(SiegeResolver.Resolve(Profile, Side(0, attacker, spy: spy), host, supply, 0.7, Side(1), place, relief, Draws(k), 18, 10));
        return list;
    }

    private static double Share(IEnumerable<SiegeResolver.Result> results, Func<SiegeResolver.Result, bool> pick)
    {
        var list = results.ToList();
        return list.Count(pick) / (double)list.Count;
    }

    private static string Ends(IEnumerable<SiegeResolver.Result> results)
        => string.Join(", ", results.GroupBy(r => r.End).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));

    [Fact]
    public void ShippedFortAndSiegeData_LoadAndNameOnlyKnownThings()
    {
        Assert.Empty(Profile.UnknownKeys);
        Assert.True(Store.Fortifications.ContainsKey("default"));
        Assert.True(Store.Sieges.ContainsKey("default"));
        foreach (var culture in Store.Cultures.Keys)
        {
            var way = Profile.WayOf(culture);
            Assert.NotNull(way.Fort);
            Assert.False(string.IsNullOrWhiteSpace(Store.Cultures[culture].Architecture), $"culture {culture} names no architecture");
        }
        // Who builds forts is data: dwarves and gnomes most, the horde and the forest least.
        Assert.True(Profile.WayOf("dwarven_clan").Fortify > Profile.WayOf("human_common").Fortify);
        Assert.True(Profile.WayOf("human_common").Fortify > Profile.WayOf("orcish_horde").Fortify);
        Assert.True(Profile.WayOf("dwarven_clan").Fort!.Walls > Profile.WayOf("orcish_horde").Fort!.Walls);
    }

    [Fact]
    public void EverySiege_EndsWithinTheLimit_AndItsRecordAgrees()
    {
        var ends = new[] { SiegeResolver.Stormed, SiegeResolver.Surrendered, SiegeResolver.Betrayed, SiegeResolver.Relieved, SiegeResolver.Lifted, SiegeResolver.Abandoned };
        var results = Run(Town(), relief: new SiegeResolver.Relief(500, 12));
        _output.WriteLine(Ends(results));
        foreach (var r in results)
        {
            Assert.InRange(r.Months, 1, 18);
            Assert.Contains(r.End, ends);
            Assert.InRange(r.AttackerLoss, 0, 1);
            Assert.InRange(r.DefenderLoss, 0, 1);
            Assert.Equal(SiegeResolver.Fell(r.End), r.Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.AttackerVictory);
            Assert.Equal(r.End == SiegeResolver.Stormed, r.Casualty > 0);
            Assert.Equal(r.End == SiegeResolver.Relieved, r.ReliefBattle is { AttackerWon: true });
            if (r.ReliefBattle != null) Assert.True(r.Months >= r.ReliefMonth);
            Assert.NotEmpty(r.Beats);
            Assert.All(r.Beats, b => Assert.True(Profile.Beats.First(x => x.Key == b).When.Any(c => c.Test == WarfareProfile.Test.SiegeEnd), $"{b} is not a siege beat"));
        }
        Assert.Contains(results, r => r.End == SiegeResolver.Relieved);
    }

    [Fact]
    public void Resupply_LengthensSieges()
    {
        var shut = Run(Town(city: true));
        var fed = Run(Town(city: true, resupply: true));
        double a = shut.Average(r => r.Months), b = fed.Average(r => r.Months);
        _output.WriteLine($"blockaded {a:F2} months ({Ends(shut)}); resupplied {b:F2} months ({Ends(fed)})");
        Assert.True(b > a, $"resupplied {b:F2} months vs blockaded {a:F2}");
        Assert.True(Share(fed, r => r.End == SiegeResolver.Surrendered) < Share(shut, r => r.End == SiegeResolver.Surrendered));
    }

    [Fact]
    public void CutSupply_RaisesTheLiftedShare()
    {
        var supplied = Run(Town(), supply: 0.9);
        var cut = Run(Town(), supply: 0.45);
        double a = Share(supplied, r => r.End == SiegeResolver.Lifted), b = Share(cut, r => r.End == SiegeResolver.Lifted);
        _output.WriteLine($"lifted: supplied {a:P1}, cut {b:P1}");
        Assert.True(b > a + 0.15, $"cut supply lifted {b:P1} vs {a:P1}");
    }

    [Fact]
    public void StrongerWalls_FallLess_AndSiegeCraftTakesMore()
    {
        // A fed city: hunger alone does not decide, the walls do (through storms).
        var weak = Run(Town(walls: 0.4, city: true, resupply: true), host: 900);
        var strong = Run(Town(walls: 1.6, city: true, resupply: true), host: 900);
        double fw = Share(weak, r => SiegeResolver.Fell(r.End)), fs = Share(strong, r => SiegeResolver.Fell(r.End));
        _output.WriteLine($"fell: weak walls {fw:P1} ({Ends(weak)}), strong walls {fs:P1} ({Ends(strong)})");
        Assert.True(fw > fs + 0.1, $"weak walls fell {fw:P1}, strong {fs:P1}");
        Assert.True(Share(weak, r => r.End == SiegeResolver.Stormed) > Share(strong, r => r.End == SiegeResolver.Stormed));
        double stormDwarf = Share(Run(Town(walls: 0.8), host: 900, attacker: "dwarven_clan"), r => r.End == SiegeResolver.Stormed);
        double stormOrc = Share(Run(Town(walls: 0.8), host: 900, attacker: "orcish_horde"), r => r.End == SiegeResolver.Stormed);
        _output.WriteLine($"stormed: dwarves {stormDwarf:P1}, orcs {stormOrc:P1}");
        Assert.True(stormDwarf > stormOrc);
    }

    [Fact]
    public void CunningSpymasters_OpenGates()
    {
        double plain = Share(Run(Town()), r => r.End == SiegeResolver.Betrayed);
        double cunning = Share(Run(Town(), spy: 90), r => r.End == SiegeResolver.Betrayed);
        _output.WriteLine($"betrayed: plain {plain:P1}, cunning spymaster {cunning:P1}");
        Assert.True(cunning > plain);
    }

    [Fact]
    public void Garrisons_AndWatchtowers_HelpTheDefence()
    {
        static double Wins(BattleResolver.Field field)
        {
            int won = 0;
            for (int k = 0; k < Keys; k++)
                if (BattleResolver.Resolve(Profile, Side(0), Side(1), new[] { field }, Draws(k), 10).DefenderWon) won++;
            return won / (double)Keys;
        }
        var open = new BattleResolver.Field(0, Profile.TerrainIndex("open"), false, 0.5, 10, 0);
        double bare = Wins(open), garrisoned = Wins(open with { DefenderGarrison = 300 }), watched = Wins(open with { Watch = 0.4 });
        _output.WriteLine($"defender wins: bare {bare:P1}, +300 garrison {garrisoned:P1}, watchtowers {watched:P1}");
        Assert.True(garrisoned > bare + 0.05);
        Assert.True(watched > bare);
    }

    [Fact]
    public void SameKey_SameSiege()
    {
        for (int k = 0; k < 100; k++)
        {
            var a = SiegeResolver.Resolve(Profile, Side(0), 500, 0.7, 0.7, Side(1), Town(), new SiegeResolver.Relief(400, 10), Draws(k), 18, 10);
            var b = SiegeResolver.Resolve(Profile, Side(0), 500, 0.7, 0.7, Side(1), Town(), new SiegeResolver.Relief(400, 10), Draws(k), 18, 10);
            Assert.Equal((a.Months, a.End, a.AttackerLoss, a.DefenderLoss, a.Casualty, string.Join(",", a.Beats)), (b.Months, b.End, b.AttackerLoss, b.DefenderLoss, b.Casualty, string.Join(",", b.Beats)));
        }
    }
}
