namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One turn's commit of the expansion kernel's proposals (persons, offices, relations, battles, route state). Owns what the
/// legacy commit does not: the pending-person map, the summed person and route deltas, the appointment and detail counters.
/// Person references in proposals may be the emitting operator's pending handles (-2, -3...); they resolve through
/// <see cref="Resolve"/>, and a proposal naming a person that was refused is itself refused.
/// </summary>
internal sealed class ExpansionCommit
{
    /// <summary>A reference that does not resolve: an unknown pending handle or a refused person.</summary>
    public const int Unresolved = int.MinValue;

    private readonly HistoryCatalog _history;
    private readonly HistoryContext _context;
    private readonly int _turn;
    private readonly Dictionary<(string Operator, int Pending), int> _pending = new();
    private readonly SortedDictionary<int, (int Renown, double Wealth, int Loyalty, double Pledged)> _personDeltas = new();
    private readonly SortedDictionary<(int A, int B), (double Safety, double Traffic, int Forts, int Waystations)> _routeDeltas = new();
    private readonly Dictionary<Proposal.AddBattle, int> _battles = new();
    private readonly Dictionary<(int Polity, string Operator), int> _detailEvents = new();
    private int _appointments;

    public ExpansionCommit(HistoryCatalog history, HistoryContext context, int turn)
    {
        _history = history;
        _context = context;
        _turn = turn;
    }

    /// <summary>A person id as the commit knows it: an id stands, a pending handle of <paramref name="op"/> maps to the id it got.</summary>
    public int Resolve(int id, string op)
    {
        if (id >= -1) return id;
        return _pending.TryGetValue((op, id), out int real) ? real : Unresolved;
    }

    private bool LivingPerson(int id) => (uint)id < (uint)_history.Persons.Count && _history.Persons[id].Alive;

    private bool LivingPolity(int id) => (uint)id < (uint)_history.Polities.Count && _history.Polities[id].Alive;

    /// <summary>
    /// The structural person pre-pass: admits the turn's <see cref="Proposal.AddPerson"/>s in canonical order under the caps
    /// (a ruler is exempt from the per-polity cap), assigns ids, then builds them with house and parent references resolved
    /// (a reference to a refused person reads -1). Returns the number refused.
    /// </summary>
    public int AddPersons(List<Proposal> proposals, HashSet<Proposal> applied)
    {
        int refused = 0;
        var living = new Dictionary<int, int>();
        foreach (var p in _history.Persons)
            if (p.Alive && p.Kind != PersonKind.Ruler && p.PolityId >= 0) living[p.PolityId] = living.GetValueOrDefault(p.PolityId) + 1;
        var admitted = new List<Proposal.AddPerson>();
        foreach (var proposal in proposals)
        {
            if (proposal is not Proposal.AddPerson ap) continue;
            bool polityOk = ap.EntityId == -1 || LivingPolity(ap.EntityId);
            bool room = _history.Persons.Count + admitted.Count < _context.MaxPersons;
            bool slot = ap.PersonKind == PersonKind.Ruler || ap.EntityId < 0
                || living.GetValueOrDefault(ap.EntityId) < _context.MaxLivingPersonsPerPolity;
            if (!polityOk || !room || !slot || ap.Pending > -2) { refused++; continue; }
            _pending[(ap.Operator, ap.Pending)] = _history.Persons.Count + admitted.Count;
            if (ap.PersonKind != PersonKind.Ruler && ap.EntityId >= 0) living[ap.EntityId] = living.GetValueOrDefault(ap.EntityId) + 1;
            admitted.Add(ap);
        }
        foreach (var ap in admitted)
        {
            int house = Resolve(ap.HouseId, ap.Operator), parent = Resolve(ap.Parent, ap.Operator);
            var person = new HistoryPerson
            {
                Id = _history.Persons.Count, Kind = ap.PersonKind, FolkKey = ap.FolkKey ?? string.Empty, CultureKey = ap.CultureKey ?? string.Empty,
                PolityId = ap.EntityId,
                SeatSettlement = ap.SeatSettlement >= -1 && ap.SeatSettlement < _history.Settlements.Count ? ap.SeatSettlement : -1,
                HouseId = house >= -1 && house < _history.Persons.Count + admitted.Count ? house : -1,
                Parent = parent >= -1 && parent < _history.Persons.Count + admitted.Count ? parent : -1,
                Feminine = ap.Feminine, BornTurn = Math.Min(ap.BornTurn, _turn), BornYear = Math.Max(0, ap.BornYear),
                Traits = ap.Traits, Martial = Skill(ap.Martial), Intrigue = Skill(ap.Intrigue), Stewardship = Skill(ap.Stewardship),
                Charisma = Skill(ap.Charisma), Renown = ap.Renown, Wealth = Finite(ap.Wealth) ? Math.Max(0, ap.Wealth) : 0,
                Loyalty = (sbyte)Math.Max(-100, Math.Min(100, (int)ap.Loyalty)), StableKey = ap.StableKey,
            };
            foreach (var epithet in ap.Epithets)
            {
                if (person.Epithets.Count == 3 || string.IsNullOrWhiteSpace(epithet)) continue;
                int id = _history.Strings.Intern(epithet);
                if (!person.Epithets.Contains(id)) person.Epithets.Add(id);
            }
            _history.Persons.Add(person);
            applied.Add(ap);
        }
        return refused;
    }

    private static byte Skill(byte value) => Math.Min(value, (byte)100);

    private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    /// <summary>Applies one expansion proposal; false when it is refused.</summary>
    public bool Apply(Proposal proposal)
    {
        switch (proposal)
        {
            case Proposal.EndPerson ep: return EndPerson(ep);
            case Proposal.SetTenure st: return SetTenure(st);
            case Proposal.AddRelation ar: return AddRelation(ar);
            case Proposal.EndRelation er: return EndRelation(er);
            case Proposal.AdjustPerson adj:
            {
                int person = Resolve(adj.Person, adj.Operator);
                if ((uint)person >= (uint)_history.Persons.Count || !Finite(adj.Wealth) || !Finite(adj.Pledged)) return false;
                var d = _personDeltas.GetValueOrDefault(person);
                _personDeltas[person] = (d.Renown + adj.Renown, d.Wealth + adj.Wealth, d.Loyalty + adj.Loyalty, d.Pledged + adj.Pledged);
                return true;
            }
            case Proposal.AddBattle ab: return AddBattle(ab);
            case Proposal.AdjustRoute ar:
            {
                if (!RouteExists(ar.RouteA, ar.RouteB) || !Finite(ar.Safety) || !Finite(ar.Traffic)) return false;
                var d = _routeDeltas.GetValueOrDefault((ar.RouteA, ar.RouteB));
                _routeDeltas[(ar.RouteA, ar.RouteB)] = (d.Safety + ar.Safety, d.Traffic + ar.Traffic, d.Forts + ar.Forts, d.Waystations + ar.Waystations);
                return true;
            }
            case Proposal.SetRouteFlag sf: return SetRouteFlag(sf);
            default: return false;
        }
    }

    private bool EndPerson(Proposal.EndPerson ep)
    {
        int id = Resolve(ep.Person, ep.Operator);
        if (!LivingPerson(id)) return false;
        var person = _history.Persons[id];
        person.DiedTurn = _turn;
        person.DeathCause = _history.Strings.Intern(string.IsNullOrWhiteSpace(ep.Cause) ? "unknown" : ep.Cause);
        // The dead hold no office.
        foreach (var t in _history.Tenures)
            if (t.Open && t.Person == id) t.ToTurn = _turn;
        return true;
    }

    private bool SetTenure(Proposal.SetTenure st)
    {
        if (string.IsNullOrWhiteSpace(st.Office) || !LivingPolity(st.EntityId)) return false;
        int person = Resolve(st.Person, st.Operator);
        if (person != -1 && !LivingPerson(person)) return false;
        bool ruler = string.Equals(st.Office, Proposal.SetTenure.RulerOffice, StringComparison.Ordinal);
        if (person >= 0 && !ruler && _appointments >= _context.MaxAppointmentsPerTurn) return false;
        int office = _history.Strings.Intern(st.Office);
        foreach (var t in _history.Tenures)
        {
            if (!t.Open || t.Polity != st.EntityId || t.Office != office) continue;
            if (t.Person == person) return false; // already holds it
            t.ToTurn = _turn;
        }
        if (person >= 0)
        {
            _history.Tenures.Add(new HistoryTenure { Person = person, Polity = st.EntityId, Office = office, FromTurn = _turn });
            if (!ruler) _appointments++;
        }
        return true;
    }

    private bool AddRelation(Proposal.AddRelation ar)
    {
        int a = Resolve(ar.A, ar.Operator), b = Resolve(ar.B, ar.Operator);
        if (a == b || !LivingPerson(a) || !LivingPerson(b) || string.IsNullOrWhiteSpace(ar.RelationKind)) return false;
        int kind = _history.Strings.Intern(ar.RelationKind);
        foreach (var r in _history.Relations)
        {
            if (r.Open && r.A == a && r.B == b && r.Kind == kind) { r.Weight += ar.Weight; return true; }
        }
        if (OpenRelations(a) >= _context.MaxOpenRelationsPerPerson || OpenRelations(b) >= _context.MaxOpenRelationsPerPerson) return false;
        var relation = new HistoryRelation { A = a, B = b, Kind = kind, FromTurn = _turn, Weight = ar.Weight };
        int at = _history.Relations.Count;
        while (at > 0 && HistoryCatalog.CompareRelations(_history.Relations[at - 1], relation) > 0) at--;
        _history.Relations.Insert(at, relation);
        return true;
    }

    private int OpenRelations(int person)
    {
        int open = 0;
        foreach (var r in _history.Relations)
            if (r.Open && (r.A == person || r.B == person)) open++;
        return open;
    }

    private bool EndRelation(Proposal.EndRelation er)
    {
        int a = Resolve(er.A, er.Operator), b = Resolve(er.B, er.Operator);
        if (string.IsNullOrWhiteSpace(er.RelationKind) || !_history.Strings.TryGetId(er.RelationKind, out int kind)) return false;
        foreach (var r in _history.Relations)
        {
            if (!r.Open || r.A != a || r.B != b || r.Kind != kind) continue;
            r.ToTurn = _turn;
            return true;
        }
        return false;
    }

    private bool AddBattle(Proposal.AddBattle ab)
    {
        var attacker = Side(ab.Attacker, ab.Operator);
        var defender = Side(ab.Defender, ab.Operator);
        if (attacker == null || defender == null) return false;
        if (!Enum.IsDefined(typeof(BattleKind), ab.BattleKind) || !Enum.IsDefined(typeof(BattleOutcome), ab.Outcome)
            || !Enum.IsDefined(typeof(HistorySeason), ab.Season) || ab.SiegeMonths < 0
            || ab.Settlement < -1 || ab.Settlement >= _history.Settlements.Count)
            return false;
        var participants = Participants(ab.Participants, ab.Operator);
        if (participants == null && ab.Participants != null) return false;
        int id = _history.Battles.Count;
        var battle = new HistoryBattle
        {
            Id = id, Turn = _turn, Year = Math.Max(0, ab.Year), Season = ab.Season, Kind = ab.BattleKind, Tile = ab.Tile,
            Terrain = InternOrNone(ab.Terrain), Settlement = ab.Settlement, Attacker = attacker, Defender = defender, Outcome = ab.Outcome,
            SiegeMonths = ab.SiegeMonths, SiegeEnd = InternOrNone(ab.SiegeEnd),
            Parent = ab.ParentBattle != null && _battles.TryGetValue(ab.ParentBattle, out int parent) ? parent : -1,
        };
        foreach (var beat in ab.Beats)
            if (!string.IsNullOrWhiteSpace(beat)) battle.Beats.Add(_history.Strings.Intern(beat));
        _history.Battles.Add(battle);
        _battles[ab] = id;
        var tags = new List<string>(ab.Tags.Count + 1);
        tags.AddRange(ab.Tags);
        tags.Add($"battle:{id}");
        battle.EventId = _history.AppendLinked(ab.EventKind, _turn, ab.EntityId, ab.Tile, tags, -1, participants);
        return true;
    }

    private BattleSide? Side(Proposal.AddBattle.Side side, string op)
    {
        if (side == null || (uint)side.Polity >= (uint)_history.Polities.Count) return null;
        int general = Resolve(side.General, op);
        if (general != -1 && (uint)general >= (uint)_history.Persons.Count) return null;
        if (!Finite(side.Strength) || side.Strength < 0 || !(side.LossShare >= 0 && side.LossShare <= 1)
            || !Finite(side.Supply) || !Finite(side.Morale))
            return null;
        return new BattleSide
        {
            Polity = side.Polity, General = general, Strength = side.Strength, LossShare = side.LossShare, Supply = side.Supply,
            Morale = side.Morale, Tactic = InternOrNone(side.Tactic), Unit = InternOrNone(side.Unit),
        };
    }

    private int InternOrNone(string name) => string.IsNullOrWhiteSpace(name) ? -1 : _history.Strings.Intern(name);

    /// <summary>
    /// The event an <see cref="Proposal.AppendEvent"/> writes, with pending person handles in <c>person:</c> tags and person
    /// participants resolved; <c>null</c> when one does not resolve, or when a detail event is past the per-polity, per-operator
    /// cap.
    /// </summary>
    public (IReadOnlyList<string> Tags, IReadOnlyList<EventParticipant>? Participants)? Event(Proposal.AppendEvent e)
    {
        bool detail = EventImportanceMap.IsDetail(e.EventKind);
        var key = (e.EntityId, e.Operator);
        if (detail && _detailEvents.GetValueOrDefault(key) >= _context.MaxDetailEventsPerPolityOperator) return null;
        var participants = Participants(e.Participants, e.Operator);
        if (participants == null && e.Participants != null) return null;
        IReadOnlyList<string> tags = e.Tags;
        for (int i = 0; i < tags.Count; i++)
        {
            string tag = tags[i];
            if (!tag.StartsWith("person:-", StringComparison.Ordinal)) continue;
            if (!int.TryParse(tag.Substring("person:".Length), System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out int pending)) continue;
            int real = Resolve(pending, e.Operator);
            if (real < 0) return null;
            if (ReferenceEquals(tags, e.Tags)) tags = new List<string>(e.Tags);
            ((List<string>)tags)[i] = $"person:{real}";
        }
        if (detail) _detailEvents[key] = _detailEvents.GetValueOrDefault(key) + 1;
        return (tags, participants);
    }

    private IReadOnlyList<EventParticipant>? Participants(IReadOnlyList<EventParticipant>? parts, string op)
    {
        if (parts == null) return null;
        List<EventParticipant>? resolved = null;
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            if (part.EntityId >= -1) continue;
            if ((uint)part.Role >= (uint)_history.Strings.Count
                || ParticipantRoles.EntityOf(_history.Strings.NameOf(part.Role)) != ParticipantEntity.Person)
                return null;
            int real = Resolve(part.EntityId, op);
            if (real < 0) return null;
            resolved ??= new List<EventParticipant>(parts);
            resolved[i] = new EventParticipant(real, part.Role);
        }
        return resolved ?? parts;
    }

    private bool RouteExists(int a, int b)
    {
        foreach (var road in _history.Roads)
            if (road.RouteA == a && road.RouteB == b) return true;
        return false;
    }

    private HistoryRouteState RouteState(int a, int b)
    {
        var states = _history.RouteStates;
        int at = 0;
        while (at < states.Count && (states[at].RouteA, states[at].RouteB).CompareTo((a, b)) < 0) at++;
        if (at < states.Count && states[at].RouteA == a && states[at].RouteB == b) return states[at];
        var state = new HistoryRouteState { RouteA = a, RouteB = b, Safety = HistoryRouteState.NewRouteSafety };
        states.Insert(at, state);
        return state;
    }

    private bool SetRouteFlag(Proposal.SetRouteFlag sf)
    {
        if (!RouteExists(sf.RouteA, sf.RouteB) || !Enum.IsDefined(typeof(RouteFlag), sf.Flag)) return false;
        int value = sf.Value;
        switch (sf.Flag)
        {
            case RouteFlag.OutlawBand:
                value = Resolve(sf.Value, sf.Operator);
                if (value != -1 && (uint)value >= (uint)_history.Persons.Count) return false;
                break;
            case RouteFlag.Sponsor:
                if (value != -1 && (uint)value >= (uint)_history.Polities.Count) return false;
                break;
            default:
                if (value < -1) return false;
                break;
        }
        var state = RouteState(sf.RouteA, sf.RouteB);
        switch (sf.Flag)
        {
            case RouteFlag.OutlawBand: state.OutlawBand = value; break;
            case RouteFlag.Sponsor: state.Sponsor = value; break;
            case RouteFlag.SabotagedTurn: state.SabotagedTurn = value; break;
            default: state.LastIncidentTurn = value; break;
        }
        return true;
    }

    /// <summary>Applies the summed person and route deltas, each clamped once, in id order.</summary>
    public void Finish()
    {
        foreach (var (id, d) in _personDeltas)
        {
            var person = _history.Persons[id];
            person.Renown += d.Renown;
            person.Wealth = Math.Max(0, person.Wealth + d.Wealth);
            person.Loyalty = (sbyte)Math.Max(-100, Math.Min(100, person.Loyalty + d.Loyalty));
            person.Pledged = Math.Max(0, person.Pledged + d.Pledged);
        }
        foreach (var ((a, b), d) in _routeDeltas)
        {
            var state = RouteState(a, b);
            state.Safety = Math.Max(0, Math.Min(1, state.Safety + d.Safety));
            state.Traffic = Math.Max(0, Math.Min(1, state.Traffic + d.Traffic));
            state.Forts = Math.Max(0, state.Forts + d.Forts);
            state.Waystations = Math.Max(0, state.Waystations + d.Waystations);
        }
    }

    /// <summary>Closes the open tenures of polities that ended this turn (the dead hold no office, nor do the fallen).</summary>
    public void CloseEndedOffices()
    {
        foreach (var t in _history.Tenures)
            if (t.Open && (uint)t.Polity < (uint)_history.Polities.Count && !_history.Polities[t.Polity].Alive) t.ToTurn = _turn;
    }
}
