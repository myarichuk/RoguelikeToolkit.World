using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 74, with <see cref="HistoryFeatures.Outlaws"/> only: outlaw bands on the roads. All tuning is data
/// (<c>Peoples/Data/Outlaws/default.yaml</c>).
/// <list type="bullet">
/// <item>A band forms from displaced people left unabsorbed for <c>DisplacedTurns</c>, from the deserters of a host routed
/// (<c>DeserterChance</c>) or beaten (<c>DefeatChance</c>) last turn, from the dispossessed of a settlement captured or razed last
/// turn, or from a ruined merchant (wealth under <c>Commerce.RuinBelow</c>; with outlaws on, this operator
/// decides every ruined merchant's end). It takes the nearest road no band works within <c>RouteDegrees</c> and hides in
/// <c>HavenTerrains</c> ground beside it. Its leader is a free person (<see cref="PersonKind.Outlaw"/>, no polity), keyed by the
/// source; the route state names the band. At most <see cref="TurnView.MaxOutlawBands"/> bands work the roads.</item>
/// <item>Each turn a band raids with <c>(1 − safety) × RaidRate × (TrafficFloor + traffic)</c>: the keeper's richest merchant on
/// the road loses <c>RaidLoss</c> of its wealth to the band, whose leader gains renown. A raid by a band a rival secretly
/// sponsors may expose the sponsor (<see cref="IntrigueDef"/>): an <c>Intrigue</c> event and a grudge, a cause for war. With
/// commerce on, nameless brigands raid unworked roads at <c>BrigandShare</c> of that.</item>
/// <item>A band ends when the keepers hang its leader, when the keeper's ruler pardons it (the leader lives on, bound to the
/// ruler), or when its leader dies of age.</item>
/// </list>
/// </summary>
public sealed class OutlawOperator : IHistoryOperator
{
    /// <summary>Death cause of a hanged outlaw.</summary>
    public const string HangedCause = "hanged";

    /// <summary>Death cause (and outlaw source) of a ruined merchant.</summary>
    public const string RuinedCause = "ruined";

    private PeoplesStore? _store;
    private bool _loaded;
    private TradeProfile? _profile;

    /// <summary>Diagnostic name, <c>Outlaw</c>.</summary>
    public string Name => "Outlaw";

    /// <summary><see cref="StageOrder.Outlaw"/> (74).</summary>
    public int Order => StageOrder.Outlaw;

    internal TradeProfile Profile => _profile ??= TradeProfile.From(_store);

    /// <summary>Proposes the turn's band ends, raids and new bands.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!view.Has(HistoryFeatures.Outlaws) || view.Polities.Count == 0) return;
        if (!_loaded)
        {
            _store = view.LoadPeoples();
            _profile = null;
            _loaded = true;
        }
        new Turn(this, view, sink).Run();
    }

    private sealed class Turn
    {
        private readonly OutlawOperator _op;
        private readonly TurnView _view;
        private readonly ProposalSink _sink;
        private readonly TradeProfile _p;
        private readonly List<TradeProfile.Route> _routes;
        private readonly HashSet<(int, int)> _taken = new();
        private int _bands;

        public Turn(OutlawOperator op, TurnView view, ProposalSink sink)
        {
            _op = op;
            _view = view;
            _sink = sink;
            _p = op.Profile;
            _routes = TradeProfile.Routes(view);
            foreach (var r in _routes)
                if (r.Band >= 0 && r.Band < view.Persons.Count && view.Persons[r.Band].Alive) { _taken.Add(r.Key); _bands++; }
        }

        private OutlawDef Data => _p.Outlaws;

        public void Run()
        {
            var bandOf = new Dictionary<int, TradeProfile.Route>();
            foreach (var r in _routes)
                if (_taken.Contains(r.Key)) bandOf[r.Band] = r;
            // The free: age takes them; a working band raids, or is hanged or pardoned.
            foreach (var person in _view.Persons)
            {
                if (!person.Alive || person.PolityId >= 0 || person.Kind != PersonKind.Outlaw) continue;
                bandOf.TryGetValue(person.Id, out var route);
                if (Aged(person, route)) continue;
                if (route == null || Ended(person, route)) continue;
                Raid(route, person);
                Sabotage(route, person);
            }
            if (_view.Has(HistoryFeatures.Commerce))
                foreach (var r in _routes)
                    if (!_taken.Contains(r.Key) && !r.Severed) Raid(r, null);
            Rise();
        }

        private int Victim(TradeProfile.Route route) => route.Keeper >= 0 ? route.Keeper : route.KeeperA >= 0 ? route.KeeperA : route.KeeperB;

        /// <summary>Whom a band preys on: a sponsored band the end of the road its sponsor holds the heavier grudge against, else the road's keeper.</summary>
        private int Prey(TradeProfile.Route route)
        {
            if (route.Sponsor < 0) return Victim(route);
            int a = route.KeeperA != route.Sponsor ? route.KeeperA : -1, b = route.KeeperB != route.Sponsor ? route.KeeperB : -1;
            if (a < 0) return b >= 0 ? b : Victim(route);
            if (b < 0) return a;
            return _view.GrudgeWeight(route.Sponsor, b) > _view.GrudgeWeight(route.Sponsor, a) ? b : a;
        }

        /// <summary>Names a sponsor: the victim's grudge, an <c>Intrigue</c> event, the sponsorship lifted.</summary>
        private void Expose(TradeProfile.Route route, int victim, int band, int tile, string how)
        {
            var intrigue = _p.Intrigue;
            _sink.Emit(new Proposal.SetRouteFlag { EntityId = -1, RouteA = route.Key.A, RouteB = route.Key.B, Flag = RouteFlag.Sponsor, Value = -1 });
            if (intrigue.ExposedGrudge > 0)
                _sink.Emit(new Proposal.AddGrudge { EntityId = victim, Other = route.Sponsor, Weight = intrigue.ExposedGrudge });
            Event(HistoryEventKind.Intrigue, victim, tile,
                new List<string> { "plot:sponsorship_exposed", $"actor:{route.Sponsor}", $"target:{victim}", $"person:{band}", $"route:{route.Key.A}-{route.Key.B}", how });
        }

        private void Event(HistoryEventKind kind, int polity, int tile, List<string> tags, params EventParticipant[] participants)
        {
            if (polity < 0) return;
            _sink.Event(kind, polity, tile, tags, participants: participants.Length > 0 ? participants : null);
        }

        private bool Aged(TurnView.PersonState person, TradeProfile.Route? route)
        {
            var folk = HistoryDemography.ResolveFolk(person.FolkKey, _op._store);
            int age = _view.Turn * _view.TurnYears - (person.BornTurn * _view.TurnYears + person.BornYear);
            bool cap = age + _view.TurnYears > _p.Court.Data.HardCap * folk.LifespanYears;
            var rng = _sink.Rng(person.StableKey, "death");
            if (!cap && rng.NextDouble() >= _p.Court.DeathChance(age, folk.LifespanYears, _view.TurnYears)) return false;
            _sink.Emit(new Proposal.EndPerson { EntityId = -1, Person = person.Id, Cause = CourtOperator.AgeCause });
            if (route != null)
            {
                Clear(route);
                Event(HistoryEventKind.OutlawEnded, Victim(route), route.Tiles[route.Tiles.Count / 2],
                    new List<string> { $"person:{person.Id}", "end:age", $"route:{route.Key.A}-{route.Key.B}" },
                    _sink.Participant(person.Id, ParticipantRoles.PersonPrefix + "outlaw"));
            }
            return true;
        }

        private void Clear(TradeProfile.Route route)
        {
            _sink.Emit(new Proposal.SetRouteFlag { EntityId = -1, RouteA = route.Key.A, RouteB = route.Key.B, Flag = RouteFlag.OutlawBand, Value = -1 });
            if (route.Sponsor >= 0)
                _sink.Emit(new Proposal.SetRouteFlag { EntityId = -1, RouteA = route.Key.A, RouteB = route.Key.B, Flag = RouteFlag.Sponsor, Value = -1 });
            _taken.Remove(route.Key);
        }

        /// <summary>The keepers hang the leader or the ruler pardons the band; true when the band ended.</summary>
        private bool Ended(TurnView.PersonState person, TradeProfile.Route route)
        {
            int keeper = Victim(route);
            if (keeper < 0) return false;
            var rng = _sink.Rng(person.StableKey, "fate");
            double hang = Data.HangBase + Data.HangSafety * route.Safety + Data.HangForts * route.Forts;
            double roll = rng.NextDouble(), pardonRoll = rng.NextDouble(), confessRoll = rng.NextDouble();
            int tile = route.Tiles[route.Tiles.Count / 2];
            var leader = _sink.Participant(person.Id, ParticipantRoles.PersonPrefix + "outlaw");
            if (roll < hang)
            {
                _sink.Emit(new Proposal.EndPerson { EntityId = -1, Person = person.Id, Cause = HangedCause });
                int prey = Prey(route);
                if (route.Sponsor >= 0 && prey >= 0 && _view.Has(HistoryFeatures.Intrigue) && confessRoll < _p.Intrigue.ConfessChance)
                    Expose(route, prey, person.Id, tile, "confession");
                Clear(route);
                Event(HistoryEventKind.OutlawEnded, keeper, tile,
                    new List<string> { $"person:{person.Id}", "end:hanged", $"by:{keeper}", $"route:{route.Key.A}-{route.Key.B}" }, leader);
                return true;
            }
            int ruler = _view.OfficeHolder(keeper, Proposal.SetTenure.RulerOffice);
            if (ruler < 0 || !_view.Persons[ruler].Alive) return false;
            double pardon = Data.PardonChance * CourtProfile.Scale(_view.Persons[ruler].Traits.Agreeableness) / 100.0;
            if (pardonRoll >= pardon) return false;
            Clear(route);
            _sink.Emit(new Proposal.AddRelation { EntityId = keeper, A = ruler, B = person.Id, RelationKind = "pardoned" });
            Event(HistoryEventKind.OutlawEnded, keeper, tile,
                new List<string> { $"person:{person.Id}", "end:pardoned", $"by:{keeper}", $"route:{route.Key.A}-{route.Key.B}" }, leader);
            return true;
        }

        /// <summary>A sponsored band cuts its road on its sponsor's account (intrigue on), with <c>SponsoredSabotage</c> a turn.</summary>
        private void Sabotage(TradeProfile.Route route, TurnView.PersonState band)
        {
            if (route.Sponsor < 0 || route.Severed || !_view.Has(HistoryFeatures.Intrigue) || route.Sabotaged >= _view.Turn - 1) return;
            if (_sink.Rng(band.StableKey, "sabotage").NextDouble() >= _p.Intrigue.SponsoredSabotage) return;
            int prey = Prey(route);
            if (prey < 0) return;
            _sink.Emit(new Proposal.SetRouteFlag { EntityId = -1, RouteA = route.Key.A, RouteB = route.Key.B, Flag = RouteFlag.SabotagedTurn, Value = _view.Turn });
            Event(HistoryEventKind.Intrigue, route.Sponsor, route.Tiles[route.Tiles.Count / 2],
                new List<string> { "plot:sabotage", $"actor:{route.Sponsor}", $"target:{prey}", $"band:{band.Id}", $"route:{route.Key.A}-{route.Key.B}", "secret" },
                _sink.Participant(band.Id, ParticipantRoles.PersonPrefix + "outlaw"));
        }

        /// <summary>A raid on <paramref name="route"/> by <paramref name="band"/> (null: nameless brigands).</summary>
        private void Raid(TradeProfile.Route route, TurnView.PersonState? band)
        {
            int victim = band != null ? Prey(route) : Victim(route);
            if (victim < 0) return;
            double chance = (1 - route.Safety) * Data.RaidRate * (Data.TrafficFloor + route.Traffic) * (band == null ? Data.BrigandShare : 1);
            var rng = band != null ? _sink.Rng(band.StableKey, "raid") : _sink.Rng(HistoryRng.StableHash($"{route.Key.A}|{route.Key.B}"), "brigands");
            double roll = rng.NextDouble(), exposeRoll = rng.NextDouble();
            if (roll >= chance) return;
            int tile = route.Tiles[route.Tiles.Count / 2];
            var tags = new List<string> { $"route:{route.Key.A}-{route.Key.B}", band != null ? $"band:{band.Id}" : "brigands", $"victim:{victim}" };
            var parts = new List<EventParticipant>();
            if (band != null) parts.Add(_sink.Participant(band.Id, ParticipantRoles.PersonPrefix + "outlaw"));
            var merchant = Richest(victim, route);
            double loot = 0;
            if (merchant != null)
            {
                loot = merchant.Wealth * Data.RaidLoss;
                _sink.Emit(new Proposal.AdjustPerson { EntityId = victim, Person = merchant.Id, Wealth = -loot });
                tags.Add($"merchant:{merchant.Id}");
                parts.Add(_sink.Participant(merchant.Id, ParticipantRoles.PersonPrefix + "merchant"));
            }
            if (band != null)
                _sink.Emit(new Proposal.AdjustPerson { EntityId = -1, Person = band.Id, Renown = Data.RaidRenown, Wealth = loot });
            _sink.Emit(new Proposal.SetRouteFlag { EntityId = -1, RouteA = route.Key.A, RouteB = route.Key.B, Flag = RouteFlag.LastIncidentTurn, Value = _view.Turn });
            Event(HistoryEventKind.CaravanRaided, victim, tile, tags, parts.ToArray());

            // A sponsored band may give its sponsor away.
            if (band == null || route.Sponsor < 0 || !_view.Has(HistoryFeatures.Intrigue)) return;
            var intrigue = _p.Intrigue;
            if (victim == route.Sponsor || exposeRoll >= intrigue.SponsorExposeBase + intrigue.SponsorExposeSpy * _p.Spy(_view, victim) / 100.0) return;
            Expose(route, victim, band.Id, tile, "raid");
        }

        private TurnView.PersonState? Richest(int polity, TradeProfile.Route route)
        {
            var vectors = _view.Map.DataStore.GetTileVectors();
            TurnView.PersonState? best = null;
            foreach (var m in TradeProfile.PersonsOf(_view, polity, PersonKind.MerchantPrince))
            {
                if (m.SeatSettlement < 0 || m.SeatSettlement >= _view.Settlements.Count) continue;
                if (!TradeProfile.Within(vectors, route, _view.Settlements[m.SeatSettlement].Tile, _p.Commerce.MerchantRouteDegrees)) continue;
                if (best == null || m.Wealth > best.Wealth) best = m;
            }
            return best;
        }

        /// <summary>New bands from the displaced, deserters and ruined merchants.</summary>
        private void Rise()
        {
            var vectors = _view.Map.DataStore.GetTileVectors();
            foreach (var g in _view.DisplacedPool)
            {
                if (g.Turn > _view.Turn - Data.DisplacedTurns || (uint)g.Origin >= (uint)_view.Settlements.Count) continue;
                int key = _sink.StableKey(g.Origin, 1000 + g.Turn);
                if (_sink.Rng(key, "rise").NextDouble() >= Data.DisplacedChance) continue;
                Form(key, g.PolityId, _view.Settlements[g.Origin].Tile, "displaced", vectors);
            }
            foreach (var b in _view.Battles)
            {
                if (b.Turn != _view.Turn - 1 || b.Kind is not (BattleKind.Field or BattleKind.Ambush)) continue;
                bool routed = b.Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.DefenderDecisive;
                int loser = b.Outcome is BattleOutcome.AttackerDecisive or BattleOutcome.AttackerVictory ? b.Defender
                    : b.Outcome is BattleOutcome.DefenderDecisive or BattleOutcome.DefenderVictory ? b.Attacker : -1;
                if (loser < 0) continue;
                int key = _sink.StableKey(b.Id, 2);
                if (_sink.Rng(key, "rise").NextDouble() >= (routed ? Data.DeserterChance : Data.DefeatChance)) continue;
                Form(key, loser, b.Tile, "deserters", vectors);
            }
            // The dispossessed: the people of a settlement taken or razed last turn.
            foreach (var polity in _view.Polities)
            {
                var events = _view.EventsOf(polity.Id);
                for (int i = events.Count - 1; i >= 0 && events[i].Turn >= _view.Turn - 1; i--)
                {
                    var e = events[i];
                    if (e.Turn != _view.Turn - 1) continue;
                    bool taken = e.Kind == HistoryEventKind.SettlementCaptured, razed = e.Kind == HistoryEventKind.SettlementEnded && e.Tags.Contains("razed");
                    if (!taken && !razed) continue;
                    int settlement = Tag(e, "settlement:"), loser = taken ? Tag(e, "from:") : polity.Id;
                    if (settlement < 0 || loser < 0) continue;
                    int key = _sink.StableKey(settlement, 4);
                    if (_sink.Rng(key, "rise").NextDouble() >= Data.DispossessedChance) continue;
                    Form(key, loser, e.TileIndex, "dispossessed", vectors);
                }
            }
            foreach (var m in _view.Persons)
            {
                if (!m.Alive || m.Kind != PersonKind.MerchantPrince || m.Wealth >= _p.Commerce.RuinBelow || m.PolityId < 0
                    || _p.HoldsOffice(_view, m.PolityId, m.Id) || !_view.Has(HistoryFeatures.Commerce)) continue;
                _sink.Emit(new Proposal.EndPerson { EntityId = m.PolityId, Person = m.Id, Cause = RuinedCause });
                int key = _sink.StableKey(m.StableKey, 3);
                if (_sink.Rng(key, "rise").NextDouble() >= Data.RuinedChance || (uint)m.SeatSettlement >= (uint)_view.Settlements.Count) continue;
                Form(key, m.PolityId, _view.Settlements[m.SeatSettlement].Tile, "ruined", vectors);
            }
        }

        private static int Tag(HistoryEvent e, string prefix)
        {
            foreach (var t in e.Tags)
                if (t.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(t.Substring(prefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int v))
                    return v;
            return -1;
        }

        private void Form(int key, int source, int tile, string why, ReadOnlySpan<Vector3D> vectors)
        {
            if (_bands >= _view.MaxOutlawBands || (uint)source >= (uint)_view.Polities.Count || _view.Persons.Count >= _view.MaxPersons - Data.PersonReserve) return;
            TradeProfile.Route? route = null;
            double best = Data.RouteDegrees;
            foreach (var r in _routes)
            {
                if (_taken.Contains(r.Key)) continue; // a severed road is the lawless kind they like best
                double d = TradeProfile.DegreesTo(vectors, r, tile);
                if (d <= best) { best = d; route = r; }
            }
            if (route == null) return;
            int haven = Haven(route, _sink.Rng(key, "haven"));
            if (haven < 0 || !_sink.TrySpendEmergence()) return;
            var polity = _view.Polities[source];
            int born = TradeProfile.BornAged(_view, _sink, _op._store, key, polity.FolkKey, Data.AgeMin, Data.AgeMax);
            var person = _p.Court.RollPerson(_sink, _op._store, key, -1, polity.FolkKey, polity.CultureKey, -1, PersonKind.Outlaw, born, -1, -1, _view.TurnYears);
            int band = _sink.ProposePerson(person);
            _taken.Add(route.Key);
            _bands++;
            _sink.Emit(new Proposal.SetRouteFlag { EntityId = -1, RouteA = route.Key.A, RouteB = route.Key.B, Flag = RouteFlag.OutlawBand, Value = band });
            Event(HistoryEventKind.OutlawRose, source, haven,
                new List<string> { $"person:{band}", $"source:{why}", $"haven:{haven}", $"route:{route.Key.A}-{route.Key.B}", $"from:{source}" },
                _sink.Participant(band, ParticipantRoles.PersonPrefix + "outlaw"));
        }

        /// <summary>A land tile of haven ground on or beside the route, within <c>HavenDegrees</c> of it; -1 when there is none.</summary>
        private int Haven(TradeProfile.Route route, Rng rng)
        {
            var store = _view.Map.DataStore;
            if (!store.IsLayerRegistered<BiomeInfo>()) return -1;
            bool hasElev = store.IsLayerRegistered<ElevationInfo>();
            var biomes = store.GetSpan<BiomeInfo>();
            var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
            var vectors = store.GetTileVectors();
            var found = new SortedSet<int>();
            Span<int> neighbors = stackalloc int[6];
            foreach (int t in route.Tiles)
            {
                int n = store.GetAdjacent(t, neighbors);
                for (int k = -1; k < n; k++)
                {
                    int c = k < 0 ? t : neighbors[k];
                    if ((uint)c >= (uint)biomes.Length || (hasElev && elev[c].Height < 0)) continue;
                    double height = hasElev ? elev[c].Height : 0;
                    int terrain = _p.Warfare.TerrainOf(biomes[c].Biome, height);
                    if ((uint)terrain < (uint)_p.Haven.Length && _p.Haven[terrain] && TradeProfile.Within(vectors, route, c, Data.HavenDegrees)) found.Add(c);
                }
            }
            if (found.Count == 0) return -1;
            return found.ElementAt((int)rng.NextUInt((uint)found.Count));
        }
    }
}
