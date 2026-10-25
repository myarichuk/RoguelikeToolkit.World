using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Peoples;
using Xunit.Abstractions;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X2 (docs/HISTORY_EXPANSION_PLAN.md): the court is an observer. With <see cref="HistoryFeatures.Persons"/> on, the legacy
/// projection is unchanged; every living polity has exactly one ruler every turn; nobody acts after death; nobody outlives
/// the age cap; and a plugin adding persons changes no court person's draws.
/// </summary>
public class CourtOperatorTests
{
    private readonly ITestOutputHelper _output;

    public CourtOperatorTests(ITestOutputHelper output) => _output = output;

    private static World Build(int seed, int size = 3, Action<HistoryDriver>? driver = null, int turns = 30)
    {
        var builder = new WorldBuilder().WithSize(size).WithSeed(seed)
            .WithHistory(c => { c.Features = HistoryFeatures.Persons; c.TurnCount = turns; });
        if (driver != null) builder.WithHistoryDriver(driver);
        return builder.Build();
    }

    private static string Name(HistoryCatalog h, int id) => h.Strings.NameOf(id);

    private static int Lifespan(HistoryPerson p) => HistoryDemography.ResolveFolk(p.FolkKey, PeoplesStore.Load()).LifespanYears;

    [Fact]
    public void ShippedCourtData_LoadsAndNamesOnlyKnownThings()
    {
        var store = PeoplesStore.Load();
        Assert.True(store.Courts.ContainsKey("default"));
        Assert.True(store.TraitTables.ContainsKey("epithets"));
        var profile = CourtProfile.From(store);
        Assert.Empty(profile.UnknownKeys);
        foreach (PolityForm form in Enum.GetValues(typeof(PolityForm)))
            Assert.NotEmpty(profile.OfficesOf(form));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    public void PersonsOn_LeavesTheLegacyProjectionUnchanged(int seed, int size)
    {
        using var off = new WorldBuilder().WithSize(size).WithSeed(seed).Build();
        using var on = Build(seed, size);
        Assert.NotEmpty(on.History.Persons);
        Assert.Equal(HistoryProjection.Hash(off.History), HistoryProjection.Hash(on.History));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void EveryLivingPolity_HasOneRulerEveryTurn(int seed)
    {
        using var world = Build(seed);
        var h = world.History;
        int ruler = h.Strings.TryGetId("ruler", out int id) ? id : -1;
        Assert.True(ruler >= 0);
        foreach (var polity in h.Polities)
        {
            int last = polity.Alive ? h.SimulatedTurns : polity.EndedTurn - 1;
            for (int t = polity.FoundedTurn + 1; t <= last; t++)
            {
                int holding = h.Tenures.Count(x => x.Polity == polity.Id && x.Office == ruler && x.FromTurn <= t && (x.ToTurn == -1 || x.ToTurn > t));
                Assert.True(holding == 1, $"polity {polity.Id} has {holding} rulers at turn {t}");
            }
        }
        Assert.All(h.Tenures.Where(t => t.Office == ruler), t => Assert.Equal(PersonKind.Ruler, h.Persons[t.Person].Kind));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void NobodyActsAfterDeath_OrOutlivesTheCap(int seed)
    {
        using var world = Build(seed);
        var h = world.History;
        int years = 10;
        var cap = PeoplesStore.Load().Courts["default"].HardCap;
        foreach (var p in h.Persons)
        {
            int end = p.Alive ? h.SimulatedTurns : p.DiedTurn;
            int age = (end - p.BornTurn) * years - p.BornYear;
            Assert.True(age <= cap * Lifespan(p), $"person {p.Id} ({p.Kind}) is {age} at turn {end}, lifespan {Lifespan(p)}");
            if (p.Alive) continue;
            if (Name(h, p.DeathCause) == "childbed") Assert.True(p.Feminine, $"person {p.Id} died in childbed");
            foreach (var t in h.Tenures.Where(t => t.Person == p.Id))
                Assert.True(t.FromTurn <= p.DiedTurn && t.ToTurn != -1 && t.ToTurn <= p.DiedTurn, $"person {p.Id} holds office after death");
            foreach (var r in h.Relations.Where(r => r.A == p.Id || r.B == p.Id))
                Assert.True(r.FromTurn <= p.DiedTurn, $"person {p.Id} bonded after death");
            foreach (var e in h.Events.Where(e => e.Tags.Contains($"person:{p.Id}")))
                Assert.True(e.Turn <= p.DiedTurn, $"person {p.Id} in an event after death");
        }
    }

    /// <summary>Adds a free wanderer every turn: ids shift, the court's draws must not.</summary>
    private sealed class Wanderers : IHistoryOperator
    {
        public string Name => "probe/wanderers";
        public int Order => 150;

        public void Propose(TurnView view, ProposalSink sink)
            => sink.ProposePerson(new Proposal.AddPerson { EntityId = -1, PersonKind = PersonKind.Outlaw, FolkKey = "human", BornTurn = view.Turn - 3, StableKey = sink.StableKey(0, 0) });
    }

    [Fact]
    public void PluginPersons_ChangeNoCourtDraws()
    {
        using var plain = Build(3);
        using var crowded = Build(3, driver: d => d.AddPluginOperator(new Wanderers()));
        string Court(HistoryCatalog h) => string.Join("\n", h.Persons.Where(p => p.PolityId >= 0).OrderBy(p => p.StableKey).Select(p =>
            $"{p.StableKey}:{p.Kind}:{p.PolityId}:{p.Traits}:{p.Martial}/{p.Intrigue}/{p.Stewardship}/{p.Charisma}:{p.BornTurn}.{p.BornYear}:{p.DiedTurn}:"
            + string.Join(",", p.Epithets.Select(e => Name(h, e)))));
        Assert.True(crowded.History.Persons.Count > plain.History.Persons.Count);
        Assert.Equal(Court(plain.History), Court(crowded.History));
    }

    [Fact]
    public void Corpus_HasHeirsOfficesEpithetsAndBonds()
    {
        int heirs = 0, offices = 0, epithets = 0, bonds = 0, deaths = 0, persons = 0;
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            using var world = Build(seed);
            var h = world.History;
            persons += h.Persons.Count;
            heirs += h.Persons.Count(p => p.Kind == PersonKind.Heir || (p.Kind == PersonKind.Ruler && p.Parent >= 0));
            offices += h.Tenures.Count(t => Name(h, t.Office) != "ruler");
            epithets += h.Persons.Count(p => p.Epithets.Count > 0);
            bonds += h.Relations.Count;
            deaths += h.Persons.Count(p => !p.Alive);
            int living = h.Polities.Count(p => p.Alive);
            _output.WriteLine($"seed {seed}: persons {h.Persons.Count}, living {h.Persons.Count(p => p.Alive)} over {living} polities, relations "
                + string.Join(",", h.Relations.GroupBy(r => Name(h, r.Kind)).Select(g => $"{g.Key}={g.Count()}"))
                + ", deaths " + string.Join(",", h.Persons.Where(p => !p.Alive).GroupBy(p => Name(h, p.DeathCause)).Select(g => $"{g.Key}={g.Count()}")));
        }
        Assert.True(heirs > 0 && offices > 0 && epithets > persons / 4 && bonds > 0 && deaths > 0,
            $"heirs {heirs} offices {offices} epithets {epithets}/{persons} bonds {bonds} deaths {deaths}");
    }

    [Fact]
    public void PersonsOff_WritesNoPersons()
    {
        using var world = new WorldBuilder().WithSize(3).WithSeed(1).Build();
        Assert.Empty(world.History.Persons);
        Assert.Empty(world.History.Tenures);
    }
}
