using RoguelikeToolkit.World.Core.Actors;
using RoguelikeToolkit.World.Core.History;

namespace RoguelikeToolkit.World.Core.Tests;

/// <summary>
/// X1 (docs/HISTORY_EXPANSION_PLAN.md): the expansion kernel. Persons with pending handles, offices, relations, battles and
/// route state commit through proposals; detail events have their own budget and compact first; schema 13 round-trips and
/// rejects corrupt references. With every feature off nothing changes (see <see cref="LegacyHistoryFixtureTests"/>).
/// </summary>
public class HistoryExpansionKernelTests
{
    private sealed class Stub : IHistoryOperator
    {
        private readonly Action<TurnView, ProposalSink> _body;
        public Stub(string name, int order, Action<TurnView, ProposalSink> body) { Name = name; Order = order; _body = body; }
        public string Name { get; }
        public int Order { get; }
        public void Propose(TurnView view, ProposalSink sink) => _body(view, sink);
    }

    private static (HistoryCatalog, HistoryContext) World(int polities = 3, int turns = 3)
    {
        var history = new HistoryCatalog(7) { Ownership = new int[16] };
        for (int i = 0; i < polities; i++) history.Polities.Add(new Polity { Id = i, CapitalTile = i });
        history.PolitiesFormed = true;
        history.Roads.Add(new HistoryRoad { TileA = 0, TileB = 1, RouteA = 0, RouteB = 2, PolityId = 0 });
        return (history, new HistoryContext { TurnCount = turns });
    }

    private static Proposal.AddPerson Person(int polity, PersonKind kind = PersonKind.Advisor, params string[] epithets) => new()
    {
        EntityId = polity, PersonKind = kind, FolkKey = "human", CultureKey = "human_common", BornTurn = -3,
        Traits = new ActorTraits(50, 60, 40, 30, 20), Martial = 70, Intrigue = 40, Stewardship = 55, Charisma = 65,
        Epithets = epithets, StableKey = 1234 + polity,
    };

    private static HistoryCatalog RoundTrip(HistoryCatalog h) => HistoryCatalog.Load(new MemoryStream(h.ToBytes()));

    private static string Name(HistoryCatalog h, int id) => h.Strings.NameOf(id);

    /// <summary>Two operators each propose persons with the same handle numbers, cross-reference them and narrate them.</summary>
    private static HistoryCatalog Rich()
    {
        var (history, ctx) = World();
        var driver = new HistoryDriver()
            .AddOperator(new Stub("Court", 38, (v, s) =>
            {
                if (v.Turn != 1) return;
                int ruler = s.ProposePerson(Person(0, PersonKind.Ruler, "cautious", "pious"));
                int marshal = s.ProposePerson(Person(0, PersonKind.General, "reckless"));
                s.Emit(new Proposal.SetTenure { EntityId = 0, Office = Proposal.SetTenure.RulerOffice, Person = ruler });
                s.Emit(new Proposal.SetTenure { EntityId = 0, Office = "marshal", Person = marshal });
                s.Emit(new Proposal.AddRelation { A = ruler, B = marshal, RelationKind = "patron", Weight = 2 });
                s.Emit(new Proposal.AdjustPerson { Person = marshal, Renown = 5, Loyalty = 30 });
                s.Emit(new Proposal.AdjustPerson { Person = marshal, Renown = 2, Loyalty = 90 });
                s.Event(HistoryEventKind.PersonRose, 0, 0, new[] { $"person:{marshal}", "marshal" },
                    participants: new[] { s.Participant(marshal, "person:appointee"), s.Participant(0, "realm") });
            }))
            .AddOperator(new Stub("Rivals", 39, (v, s) =>
            {
                if (v.Turn != 1) return;
                int rival = s.ProposePerson(Person(1, PersonKind.Ruler)); // the same handle (-2) as Court's ruler
                s.Emit(new Proposal.SetTenure { EntityId = 1, Office = Proposal.SetTenure.RulerOffice, Person = rival });
            }))
            .AddOperator(new Stub("War", 50, (v, s) =>
            {
                if (v.Turn != 2) return;
                int marshal = v.OfficeHolder(0, "marshal"), rival = v.OfficeHolder(1, Proposal.SetTenure.RulerOffice);
                var siege = new Proposal.AddBattle
                {
                    EntityId = 0, EventKind = HistoryEventKind.Siege, Tags = new[] { "siege" }, Year = 3, Season = HistorySeason.Autumn,
                    BattleKind = BattleKind.Siege, Tile = 1, Terrain = "hills",
                    Attacker = new Proposal.AddBattle.Side(0, marshal, 120, 0.1, 0.8, 0.9, "siege_engines", "levy"),
                    Defender = new Proposal.AddBattle.Side(1, rival, 60, 0.3, 0.5, 0.7, "shieldwall"),
                    Outcome = BattleOutcome.AttackerVictory, Beats = new[] { "night_attack" }, SiegeMonths = 7, SiegeEnd = "stormed",
                };
                s.Emit(siege);
                s.Emit(new Proposal.AddBattle
                {
                    EntityId = 0, EventKind = HistoryEventKind.War, Year = 3, Season = HistorySeason.Autumn, BattleKind = BattleKind.Relief,
                    Tile = 2, Attacker = new Proposal.AddBattle.Side(1, -1, 40, 0.5, 0.6, 0.8),
                    Defender = new Proposal.AddBattle.Side(0, marshal, 100, 0.05, 0.8, 1.0), Outcome = BattleOutcome.DefenderDecisive,
                    ParentBattle = siege,
                });
                s.Emit(new Proposal.AdjustRoute { EntityId = 0, RouteA = 0, RouteB = 2, Safety = -0.1, Traffic = 0.4, Forts = 1 });
                s.Emit(new Proposal.AdjustRoute { EntityId = 1, RouteA = 0, RouteB = 2, Safety = -0.15, Forts = -3 });
                s.Emit(new Proposal.SetRouteFlag { EntityId = 1, RouteA = 0, RouteB = 2, Flag = RouteFlag.OutlawBand, Value = marshal }); // a sponsor pays a band
                s.Emit(new Proposal.SetRouteFlag { EntityId = 1, RouteA = 0, RouteB = 2, Flag = RouteFlag.Sponsor, Value = 1 });
                s.Emit(new Proposal.EndPerson { EntityId = 1, Person = rival, Cause = "battle" });
            }));
        driver.Apply(null!, history, ctx);
        return history;
    }

    [Fact]
    public void PendingHandles_ResolvePerOperator()
    {
        var h = Rich();
        Assert.Equal(3, h.Persons.Count);
        // Canonical order is (entity, kind, operator, sequence): polity 0's two persons, then polity 1's.
        Assert.Equal(new[] { 0, 0, 1 }, h.Persons.Select(p => p.PolityId));
        Assert.Equal(new[] { PersonKind.Ruler, PersonKind.General, PersonKind.Ruler }, h.Persons.Select(p => p.Kind));
        Assert.Equal(new[] { "cautious", "pious" }, h.Persons[0].Epithets.Select(e => Name(h, e)));

        var rulers = h.Tenures.Where(t => Name(h, t.Office) == "ruler").ToList();
        Assert.Equal(new[] { (0, 0), (2, 1) }, rulers.Select(t => (t.Person, t.Polity)));
        var patron = Assert.Single(h.Relations);
        Assert.Equal((0, 1, "patron", 2), (patron.A, patron.B, Name(h, patron.Kind), patron.Weight));

        var rose = Assert.Single(h.Events, e => e.Kind == HistoryEventKind.PersonRose);
        Assert.Contains("person:1", rose.Tags);
        Assert.Equal(new[] { (1, "person:appointee"), (0, "realm") }, rose.Participants.Select(p => (p.EntityId, Name(h, p.Role))));
    }

    [Fact]
    public void PersonDeltas_SumThenClampOnce()
    {
        var marshal = Rich().Persons[1];
        Assert.Equal(7, marshal.Renown);
        Assert.Equal(100, marshal.Loyalty); // 30 + 90, clamped once
    }

    [Fact]
    public void Battles_RecordSidesNamesAndTheirEvent()
    {
        var h = Rich();
        Assert.Equal(2, h.Battles.Count);
        var siege = h.Battles[0];
        Assert.Equal((BattleKind.Siege, 1, 2, 1), (siege.Kind, siege.Attacker.General, siege.Defender.General, siege.Defender.Polity));
        Assert.Equal(("hills", "siege_engines", "shieldwall", "stormed"),
            (Name(h, siege.Terrain), Name(h, siege.Attacker.Tactic), Name(h, siege.Defender.Tactic), Name(h, siege.SiegeEnd)));
        Assert.Equal(-1, siege.Defender.Unit);
        var narrated = h.Events.Single(e => e.Id == siege.EventId);
        Assert.Equal(HistoryEventKind.Siege, narrated.Kind);
        Assert.Contains("battle:0", narrated.Tags);
        Assert.Equal(0, h.Battles[1].Parent); // the relief battle points at its siege
    }

    [Fact]
    public void EndPerson_ClosesTheirOffices()
    {
        var h = Rich();
        var rival = h.Persons[2];
        Assert.Equal((2, "battle"), (rival.DiedTurn, Name(h, rival.DeathCause)));
        Assert.All(h.Tenures.Where(t => t.Person == 2), t => Assert.Equal(2, t.ToTurn));
    }

    [Fact]
    public void RouteDeltas_SumThenClampOnce_AndAreSampled()
    {
        var h = Rich();
        var route = Assert.Single(h.RouteStates);
        Assert.Equal(HistoryRouteState.NewRouteSafety - 0.25, route.Safety, 12);
        Assert.Equal(0.4, route.Traffic, 12);
        Assert.Equal(0, route.Forts); // +1 - 3, floored once
        Assert.Equal(1, route.Sponsor);
        Assert.Equal(new[] { 2 }, h.RouteSamples.Select(r => r.Turn)); // sampled from the turn it appeared on
    }

    [Fact]
    public void RichKernel_RoundTripsByteForByte()
    {
        var h = Rich();
        var loaded = RoundTrip(h);
        Assert.Equal(h.ToBytes(), loaded.ToBytes());
        Assert.Equal(h.Persons.Select(p => p.StableKey), loaded.Persons.Select(p => p.StableKey));
        Assert.Equal("stormed", Name(loaded, loaded.Battles[0].SiegeEnd));
    }

    [Fact]
    public void RefusedPerson_DropsWhatNamesIt()
    {
        var (history, ctx) = World(turns: 2);
        ctx.MaxLivingPersonsPerPolity = 1;
        var driver = new HistoryDriver().AddOperator(new Stub("Court", 38, (v, s) =>
        {
            int ruler = s.ProposePerson(Person(0, PersonKind.Ruler)); // exempt from the cap
            int a = s.ProposePerson(Person(0));
            int b = s.ProposePerson(Person(0)); // over the cap
            s.Emit(new Proposal.SetTenure { EntityId = 0, Office = "chancellor", Person = b });
            s.Emit(new Proposal.AddRelation { A = a, B = b, RelationKind = "rival" });
            s.Event(HistoryEventKind.PersonRose, 0, -1, new[] { $"person:{b}" });
            s.Event(HistoryEventKind.PersonRose, 0, -1, new[] { $"person:{a}" });
            s.Emit(new Proposal.SetTenure { EntityId = 0, Office = "ruler", Person = ruler });
        }));
        driver.Apply(null!, history, ctx);
        Assert.Equal(2, history.Persons.Count);
        Assert.Single(history.Tenures); // the ruler's; the refused chancellor's appointment is dropped
        Assert.Empty(history.Relations);
        Assert.Equal("person:1", Assert.Single(history.Events).Tags[0]);
        Assert.Equal(4, driver.RejectedProposals); // the person, then its appointment, its relation and its event
    }

    [Fact]
    public void DetailEvents_HaveTheirOwnBudget_AndAPerPolityCap()
    {
        var (history, ctx) = World(turns: 2);
        history.SyncBudgets(2, 100, 3);
        var driver = new HistoryDriver()
            .AddOperator(new Stub("A", 30, (v, s) =>
            {
                for (int i = 0; i < 3; i++) s.Event(HistoryEventKind.War, 0, -1, new[] { "war" });
                for (int i = 0; i < 4; i++) s.Event(HistoryEventKind.Campaign, 0, -1, new[] { "camp0" }); // 2 pass the polity cap
                for (int i = 0; i < 2; i++) s.Event(HistoryEventKind.Campaign, 1, -1, new[] { "camp1" });
            }));
        driver.Apply(null!, history, ctx);
        Assert.Equal(2, history.Events.Count(e => e.Kind == HistoryEventKind.War)); // detail took none of the 2
        Assert.Equal(1, history.DroppedEvents);
        Assert.Equal(2, history.Events.Count(e => e.Tags[0] == "camp0"));
        Assert.Equal(1, history.Events.Count(e => e.Tags[0] == "camp1")); // the detail budget of 3 ran out
        Assert.Equal(1, history.DroppedDetailEvents);
    }

    [Fact]
    public void Compaction_TakesDetailFirst_AndLeavesTheLegacyCounterAlone()
    {
        var h = new HistoryCatalog(1);
        h.SyncBudgets(100, 4);
        h.AppendLinked(HistoryEventKind.Exchange, 1, 0, -1, new[] { "minor" }, -1, null);
        h.AppendLinked(HistoryEventKind.Campaign, 1, 0, -1, new[] { "detail-a" }, -1, null);
        h.AppendLinked(HistoryEventKind.War, 1, 0, -1, new[] { "war" }, -1, null);
        h.AppendLinked(HistoryEventKind.Intrigue, 1, 0, -1, new[] { "detail-b" }, -1, null);
        h.AppendLinked(HistoryEventKind.War, 1, 0, -1, new[] { "war2" }, -1, null);
        h.AppendLinked(HistoryEventKind.War, 1, 0, -1, new[] { "war3" }, -1, null);
        Assert.Equal(new[] { "minor", "war", "war2", "war3" }, h.Events.Select(e => e.Tags[0]));
        Assert.Equal(0, h.Compactions);
        h.AppendLinked(HistoryEventKind.War, 1, 0, -1, new[] { "war4" }, -1, null);
        Assert.Equal(new[] { "war", "war2", "war3", "war4" }, h.Events.Select(e => e.Tags[0]));
        Assert.Equal(1, h.Compactions);
    }

    [Fact]
    public void EndedPolity_ClosesItsOffices_AndTheDeadCannotBeAppointed()
    {
        var (history, ctx) = World(turns: 4);
        var driver = new HistoryDriver().AddOperator(new Stub("Court", 38, (v, s) =>
        {
            if (v.Turn == 1)
            {
                int p = s.ProposePerson(Person(2, PersonKind.Ruler));
                s.Emit(new Proposal.SetTenure { EntityId = 2, Office = "ruler", Person = p });
                int q = s.ProposePerson(Person(1));
                s.Emit(new Proposal.EndPerson { EntityId = 1, Person = q, Cause = "age" });
            }
            if (v.Turn == 2) s.Emit(new Proposal.SetTenure { EntityId = 1, Office = "chancellor", Person = v.Persons.Single(x => !x.Alive).Id });
        }));
        driver.RunTurn(null!, history, ctx, 1);
        history.Polities[2].EndedTurn = 2; // ended between turns (a collapse needs a map to re-flood)
        driver.RunTurn(null!, history, ctx, 2);
        Assert.Equal(2, Assert.Single(history.Tenures).ToTurn);
        Assert.Equal(1, history.Persons.Single(x => !x.Alive).DiedTurn);
        Assert.Equal(1, driver.RejectedProposals);
    }

    [Fact]
    public void OlderFiles_LoadWithAnEmptyKernel()
    {
        var (history, _) = World();
        byte[] v13 = history.ToBytes();
        // An empty kernel section is two ints of budget and six zero counts; a v12 file is the same bytes without it.
        const int section = 8 * 4;
        byte[] v12 = v13.Take(v13.Length - section).ToArray();
        BitConverter.GetBytes(12).CopyTo(v12, 4);
        var loaded = HistoryCatalog.Load(new MemoryStream(v12));
        Assert.Empty(loaded.Persons);
        Assert.Empty(loaded.Battles);
        Assert.Equal(v13, loaded.ToBytes());
    }

    public static IEnumerable<object[]> Corruptions() => new[]
    {
        new object[] { "relation to a missing person", (Action<HistoryCatalog>)(h => h.Relations.Add(new HistoryRelation { A = 0, B = 99, Kind = 0 })) },
        new object[] { "relations out of order", (Action<HistoryCatalog>)(h => h.Relations.Insert(0, new HistoryRelation { A = 2, B = 0, Kind = 0 })) },
        new object[] { "two open tenures of an office", (Action<HistoryCatalog>)(h => h.Tenures.Add(new HistoryTenure { Person = 1, Polity = 0, Office = h.Tenures[0].Office })) },
        new object[] { "loss share above one", (Action<HistoryCatalog>)(h => h.Battles.Add(new HistoryBattle { Id = 2, Attacker = new BattleSide { Polity = 0, LossShare = 1.5 }, Defender = new BattleSide { Polity = 1 } })) },
        new object[] { "battle general missing", (Action<HistoryCatalog>)(h => h.Battles.Add(new HistoryBattle { Id = 2, Attacker = new BattleSide { Polity = 0, General = 50 }, Defender = new BattleSide { Polity = 1 } })) },
        new object[] { "person dies before birth", (Action<HistoryCatalog>)(h => h.Persons[0].DiedTurn = -10) },
        new object[] { "person polity missing", (Action<HistoryCatalog>)(h => h.Persons[0].PolityId = 40) },
        new object[] { "person participant missing", (Action<HistoryCatalog>)(h => h.Events.Single(e => e.Kind == HistoryEventKind.PersonRose).Participants[0] = new EventParticipant(77, h.Strings.Intern("person:appointee"))) },
        new object[] { "route safety above one", (Action<HistoryCatalog>)(h => h.RouteStates[0].Safety = 1.2) },
        new object[] { "route sponsor missing", (Action<HistoryCatalog>)(h => h.RouteStates[0].Sponsor = 12) },
    };

    [Theory]
    [MemberData(nameof(Corruptions))]
    public void CorruptKernel_IsRejected(string what, Action<HistoryCatalog> corrupt)
    {
        var h = Rich();
        corrupt(h);
        var ex = Assert.Throws<InvalidDataException>(() => RoundTrip(h));
        Assert.False(string.IsNullOrEmpty(what + ex.Message));
    }

    [Fact]
    public void FeaturesDefaultOff_AndTheViewCarriesThem()
    {
        Assert.Equal(HistoryFeatures.None, new HistoryContext().Features);
        var (history, ctx) = World(turns: 2);
        ctx.Features = HistoryFeatures.Persons | HistoryFeatures.Battles;
        HistoryFeatures seen = HistoryFeatures.None;
        bool sinkHas = false;
        new HistoryDriver().AddOperator(new Stub("Probe", 30, (v, s) => { seen = v.Features; sinkHas = s.Has(HistoryFeatures.Battles); }))
            .Apply(null!, history, ctx);
        Assert.Equal(HistoryFeatures.Persons | HistoryFeatures.Battles, seen);
        Assert.True(sinkHas);
    }

    [Fact]
    public void StableKey_DependsOnOperatorTurnOriginAndSlot_NotOnOthers()
    {
        var ctx = new HistoryContext();
        var a = new ProposalSink("Court", 4, 1, ctx, new List<Proposal>());
        var b = new ProposalSink("Court", 4, 1, ctx, new List<Proposal>());
        b.ProposePerson(Person(0)); // another person proposed first changes nothing
        Assert.Equal(a.StableKey(3, 0), b.StableKey(3, 0));
        Assert.NotEqual(a.StableKey(3, 0), a.StableKey(3, 1));
        Assert.NotEqual(a.StableKey(3, 0), new ProposalSink("War", 4, 1, ctx, new List<Proposal>()).StableKey(3, 0));
    }
}
