using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 82, with <see cref="HistoryFeatures.Commerce"/> only: the peaceful flows. All tuning is data
/// (<c>Peoples/Data/Commerce/default.yaml</c>).
/// <list type="bullet">
/// <item>Every route's traffic drifts toward <c>(TrafficBase + TrafficWealth × min(1, w / WealthScale)) × safety</c>, w being the
/// wealth of the merchants seated on it, times <c>SeveredTraffic</c> while it is severed.</item>
/// <item>A severed route with merchants of both keepers on it is smuggled across with <c>SmuggleChance</c> a turn; each
/// smuggler gains <c>SmuggleGain</c>.</item>
/// <item>A seafaring polity (<see cref="WarfareProfile.Sails"/>) with a grudge of at least <c>PrivateerGrudge</c> against a rival
/// whose coast lies within <c>PrivateerDegrees</c> of its own commissions a privateer (<see cref="PersonKind.Privateer"/>,
/// bound to its ruler); each turn a privateer takes a prize with <c>PrivateerRaidChance</c>, <c>PrivateerLoot</c> of the rival's
/// richest merchant's wealth.</item>
/// </list>
/// </summary>
public sealed class CommerceOperator : IHistoryOperator
{
    private PeoplesStore? _store;
    private bool _loaded;
    private TradeProfile? _profile;

    /// <summary>Diagnostic name, <c>Commerce</c>.</summary>
    public string Name => "Commerce";

    /// <summary><see cref="StageOrder.Commerce"/> (82).</summary>
    public int Order => StageOrder.Commerce;

    internal TradeProfile Profile => _profile ??= TradeProfile.From(_store);

    /// <summary>Proposes the turn's traffic drift, smuggling and privateering.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!view.Has(HistoryFeatures.Commerce) || view.Polities.Count == 0) return;
        if (!_loaded)
        {
            _store = view.LoadPeoples();
            _profile = null;
            _loaded = true;
        }
        var p = Profile;
        var routes = TradeProfile.Routes(view);
        Traffic(view, sink, p, routes);
        foreach (var polity in view.Polities)
            if (polity.Alive) Privateers(view, sink, p, polity);
    }

    private static List<TurnView.PersonState> MerchantsOn(TurnView view, TradeProfile p, TradeProfile.Route route, int polity)
    {
        var vectors = view.Map.DataStore.GetTileVectors();
        var list = new List<TurnView.PersonState>();
        if (polity < 0) return list;
        foreach (var m in TradeProfile.PersonsOf(view, polity, PersonKind.MerchantPrince))
            if ((uint)m.SeatSettlement < (uint)view.Settlements.Count
                && TradeProfile.Within(vectors, route, view.Settlements[m.SeatSettlement].Tile, p.Commerce.MerchantRouteDegrees))
                list.Add(m);
        return list;
    }

    private static void Traffic(TurnView view, ProposalSink sink, TradeProfile p, List<TradeProfile.Route> routes)
    {
        var c = p.Commerce;
        foreach (var route in routes)
        {
            var a = MerchantsOn(view, p, route, route.KeeperA);
            var b = route.KeeperB != route.KeeperA ? MerchantsOn(view, p, route, route.KeeperB) : new List<TurnView.PersonState>();
            double wealth = a.Sum(m => m.Wealth) + b.Sum(m => m.Wealth);
            double target = (c.TrafficBase + c.TrafficWealth * Math.Min(1, wealth / c.WealthScale)) * route.Safety * (route.Severed ? c.SeveredTraffic : 1);
            double drift = c.TrafficDrift * (Math.Max(0, Math.Min(1, target)) - route.Traffic);
            if (Math.Abs(drift) > 1e-9)
                sink.Emit(new Proposal.AdjustRoute { EntityId = Math.Max(-1, route.Keeper), RouteA = route.Key.A, RouteB = route.Key.B, Traffic = drift });
            if (!route.Severed || a.Count == 0 || b.Count == 0) continue;
            if (sink.Rng(HistoryRng.StableHash($"{route.Key.A}|{route.Key.B}"), "smuggle").NextDouble() >= c.SmuggleChance) continue;
            var smugglers = new[] { a[0], b[0] };
            foreach (var m in smugglers) sink.Emit(new Proposal.AdjustPerson { EntityId = m.PolityId, Person = m.Id, Wealth = c.SmuggleGain });
            sink.Event(HistoryEventKind.Smuggling, route.KeeperA, route.Tiles[route.Tiles.Count / 2],
                new[] { $"route:{route.Key.A}-{route.Key.B}", $"merchant:{a[0].Id}", $"merchant:{b[0].Id}" },
                participants: new[]
                {
                    sink.Participant(a[0].Id, ParticipantRoles.PersonPrefix + "merchant"),
                    sink.Participant(b[0].Id, ParticipantRoles.PersonPrefix + "merchant"),
                });
        }
    }

    /// <summary>The rival a seafaring polity preys on: the heaviest grudge at least <c>PrivateerGrudge</c> with a coast in reach; -1 for none.</summary>
    private static int Prey(TurnView view, TradeProfile p, TurnView.PolityState polity)
    {
        var c = p.Commerce;
        var vectors = view.Map.DataStore.GetTileVectors();
        var coast = new List<int>();
        foreach (var s in view.Settlements)
            if (!s.Ended && s.PolityId == polity.Id && TradeProfile.Coastal(view, s.Tile)) coast.Add(s.Tile);
        if (coast.Count == 0) return -1;
        int prey = -1, heaviest = Math.Max(1, c.PrivateerGrudge) - 1;
        foreach (var other in view.Polities)
        {
            if (!other.Alive || other.Id == polity.Id || view.IsWindowed(polity.Id, other.Id)) continue;
            int g = view.GrudgeWeight(polity.Id, other.Id);
            if (g <= heaviest) continue;
            bool reach = false;
            foreach (var s in view.Settlements)
            {
                if (s.Ended || s.PolityId != other.Id || !TradeProfile.Coastal(view, s.Tile)) continue;
                foreach (int t in coast)
                    if (TradeProfile.Degrees(vectors[t], vectors[s.Tile]) <= c.PrivateerDegrees) { reach = true; break; }
                if (reach) break;
            }
            if (reach) { heaviest = g; prey = other.Id; }
        }
        return prey;
    }

    private void Privateers(TurnView view, ProposalSink sink, TradeProfile p, TurnView.PolityState polity)
    {
        var c = p.Commerce;
        if (!p.Warfare.Sails(polity.Form, p.Warfare.WayOf(polity.CultureKey))) return;
        int prey = Prey(view, p, polity);
        if (prey < 0) return;
        var privateers = TradeProfile.PersonsOf(view, polity.Id, PersonKind.Privateer);
        var rng = sink.Rng(polity.Id, "privateer");
        double commission = rng.NextDouble();
        foreach (var privateer in privateers)
        {
            var prize = sink.Rng(privateer.StableKey, "prize");
            if (prize.NextDouble() >= c.PrivateerRaidChance) continue;
            TurnView.PersonState? victim = null;
            foreach (var m in TradeProfile.PersonsOf(view, prey, PersonKind.MerchantPrince))
                if (victim == null || m.Wealth > victim.Wealth) victim = m;
            double loot = victim != null ? victim.Wealth * c.PrivateerLoot : 0;
            if (victim != null) sink.Emit(new Proposal.AdjustPerson { EntityId = prey, Person = victim.Id, Wealth = -loot });
            sink.Emit(new Proposal.AdjustPerson { EntityId = polity.Id, Person = privateer.Id, Wealth = loot, Renown = 1 });
            var tags = new List<string> { $"privateer:{privateer.Id}", $"sponsor:{polity.Id}", $"victim:{prey}", "prize" };
            var parts = new List<EventParticipant> { sink.Participant(privateer.Id, ParticipantRoles.PersonPrefix + "privateer") };
            if (victim != null)
            {
                tags.Add($"merchant:{victim.Id}");
                parts.Add(sink.Participant(victim.Id, ParticipantRoles.PersonPrefix + "merchant"));
            }
            sink.Event(HistoryEventKind.Privateering, polity.Id, view.Polities[prey].CapitalTile, tags, participants: parts);
        }
        if (privateers.Count >= c.MaxPrivateers || commission >= c.PrivateerChance || view.Persons.Count >= view.MaxPersons - c.PersonReserve) return;
        int living = 0;
        foreach (var person in view.PersonsOf(polity.Id))
            if (person.Kind != PersonKind.Ruler) living++;
        int ruler = view.OfficeHolder(polity.Id, Proposal.SetTenure.RulerOffice);
        if (living >= view.MaxLivingPersonsPerPolity - c.CourtReserve || ruler < 0 || !sink.TrySpendEmergence()) return;
        int key = sink.StableKey(polity.Id, 100 + prey);
        int born = TradeProfile.BornAged(view, sink, _store, key, polity.FolkKey, c.AgeMin, c.AgeMax);
        int seat = -1;
        foreach (var s in view.Settlements)
            if (!s.Ended && s.PolityId == polity.Id && TradeProfile.Coastal(view, s.Tile)) { seat = s.Index; break; }
        int id = sink.ProposePerson(p.Court.RollPerson(sink, _store, key, polity.Id, polity.FolkKey, polity.CultureKey, seat, PersonKind.Privateer,
            born, -1, -1, view.TurnYears));
        sink.Emit(new Proposal.AddRelation { EntityId = polity.Id, A = ruler, B = id, RelationKind = "sponsor" });
        sink.Event(HistoryEventKind.Privateering, polity.Id, view.Polities[prey].CapitalTile,
            new[] { $"person:{id}", $"sponsor:{polity.Id}", $"victim:{prey}", "commission" },
            participants: new[] { sink.Participant(id, ParticipantRoles.PersonPrefix + "privateer") });
    }
}
