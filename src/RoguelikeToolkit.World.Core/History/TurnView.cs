using RoguelikeToolkit.World.Core.Actors;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Read-only snapshot of the end-of-turn <c>t-1</c> state, built once per turn by
/// <see cref="HistoryDriver"/> before any operator runs. Mutable catalog entries are copied into
/// immutable records; the event log is only reachable through index queries. Nothing an operator
/// proposes is visible here until the next turn's view.
/// </summary>
public sealed class TurnView
{
    private readonly WorldMap? _map;
    private readonly HistoryCatalog _history;
    private readonly int[] _ownership;
    private readonly Dictionary<(int, int), int> _grudges = new();
    private readonly List<(int A, int B, int Until)> _windows = new();

    /// <summary>One polity at the snapshot: identity, lifecycle and a copy of its scalars.</summary>
    public sealed record PolityState(int Id, PolityForm Form, int CapitalTile, string FolkKey, string CultureKey,
        bool Alive, int Parent, double Population, IReadOnlyList<double> Scalars, string CultureOverlay = "", int FoundedTurn = 0, int HeirBornTurn = -1, bool HasVillain = false, int RulerGeneration = 0);

    /// <summary>One road segment at the snapshot.</summary>
    public readonly record struct RoadState(int TileA, int TileB, int PolityId, int RouteA, int RouteB, bool Severed);

    /// <summary>One scar at the snapshot.</summary>
    public readonly record struct ScarState(int Tile, int PolityId, string Kind, bool Active);

    /// <summary>One holy site at the snapshot.</summary>
    public readonly record struct HolySiteState(int Tile, int PolityId, GeoCoord Position, bool Active);

    /// <summary>One settlement at the snapshot; <see cref="Index"/> is its position in <see cref="HistoryCatalog.Settlements"/>.</summary>
    public readonly record struct SettlementState(int Index, int Tile, int PolityId, SettlementTier Tier, SettlementRole Role, int Parent, int FoundedTurn, bool Ended, double Population = 0);

    /// <summary>One displaced group at the snapshot; <see cref="Index"/> is its position in <see cref="HistoryCatalog.Displaced"/>.</summary>
    public readonly record struct DisplacedState(int Index, int PolityId, string Culture, int Turn, double Amount, int Origin, int Cause);

    /// <summary>One person at the snapshot; <see cref="Id"/> is its index in <see cref="HistoryCatalog.Persons"/>. Epithets are names.</summary>
    public sealed record PersonState(int Id, PersonKind Kind, string FolkKey, string CultureKey, int PolityId, int SeatSettlement,
        int HouseId, int Parent, bool Feminine, int BornTurn, int BornYear, bool Alive, ActorTraits Traits, byte Martial, byte Intrigue,
        byte Stewardship, byte Charisma, IReadOnlyList<string> Epithets, int Renown, double Wealth, sbyte Loyalty, double Pledged,
        int StableKey);

    /// <summary>One risen villain at the snapshot.</summary>
    public readonly record struct VillainState(int Id, int PolityId, string FolkKey, int RisenTurn, string Agenda);

    /// <summary>One open bond at the snapshot; the kind is a name.</summary>
    public readonly record struct RelationState(int A, int B, string Kind, int FromTurn, int Weight);

    /// <summary>One engagement at the snapshot (the parts operators decide on; the full record is in <see cref="HistoryCatalog.Battles"/>).</summary>
    public readonly record struct BattleState(int Id, int Turn, BattleKind Kind, BattleOutcome Outcome, int Attacker, int Defender,
        int GeneralA, int GeneralD, int Tile, int Settlement, int Parent = -1, string SiegeEnd = "");

    /// <summary>One route's state at the snapshot.</summary>
    public readonly record struct RouteStateView(int RouteA, int RouteB, double Safety, double Traffic, int Forts, int Waystations,
        int OutlawBand, int Sponsor, int SabotagedTurn, int LastIncidentTurn);

    private readonly HashSet<int> _siteTiles = new();
    private readonly List<RelationState> _openRelations = new();
    private readonly Dictionary<(int Polity, string Office), int> _offices = new();

    internal TurnView(WorldMap? map, HistoryCatalog history, HistoryContext context, int turn)
    {
        _map = map;
        _history = history;
        Turn = turn;
        Seed = history.Seed;
        TurnYears = context.TurnYears;
        MaxPolities = context.MaxPolities;
        MaxVillains = context.MaxVillains;
        MaxSuccessors = context.MaxSuccessors;
        MaxRebellions = context.MaxRebellions;
        RebelCount = history.RebelCount();
        VillainCount = history.Villains.Count;
        Scalars = history.Scalars;
        _ownership = (int[])history.Ownership.Clone();
        var polities = new List<PolityState>(history.Polities.Count);
        foreach (var p in history.Polities)
        {
            var scalars = new double[history.Scalars.Count];
            for (int i = 0; i < scalars.Length; i++) scalars[i] = p.GetScalar(i, history.Scalars);
            polities.Add(new PolityState(p.Id, p.Form, p.CapitalTile, p.FolkKey, p.CultureKey, p.Alive, p.Parent, p.Population, scalars, p.CultureOverlay ?? string.Empty, p.FoundedTurn, p.HeirBornTurn, p.HasVillain, p.RulerGeneration));
        }
        Polities = polities;
        SuccessorCount = history.SuccessorCount();
        foreach (var g in history.Grudges) _grudges[(g.A, g.B)] = g.Weight;
        foreach (var w in history.Treaties) _windows.Add((w.A, w.B, w.UntilTurn));
        var roads = new List<RoadState>(history.Roads.Count);
        foreach (var r in history.Roads) roads.Add(new RoadState(r.TileA, r.TileB, r.PolityId, r.RouteA, r.RouteB, r.Severed));
        Roads = roads;
        var sites = new List<HolySiteState>(history.HolySites.Count);
        foreach (var s in history.HolySites) sites.Add(new HolySiteState(s.TileIndex, s.PolityId, s.Position, s.Active));
        HolySites = sites;
        var settlements = new List<SettlementState>(history.Settlements.Count);
        for (int i = 0; i < history.Settlements.Count; i++)
        {
            var s = history.Settlements[i];
            settlements.Add(new SettlementState(i, s.TileIndex, s.PolityId, s.Tier, s.Role, s.Parent, s.FoundedTurn, s.Ended, s.Population));
        }
        Settlements = settlements;
        var pool = new List<DisplacedState>(history.Displaced.Count);
        for (int i = 0; i < history.Displaced.Count; i++)
        {
            var g = history.Displaced[i];
            pool.Add(new DisplacedState(i, g.PolityId, g.Culture, g.Turn, g.Amount, g.Origin, g.Cause));
        }
        DisplacedPool = pool;
        DisplacedDecayTurns = context.DisplacedDecayTurns;
        MaxSettlementsPerPolity = context.MaxSettlementsPerPolity;
        HazardRate = context.HazardRate;
        foreach (var s in history.Sites) _siteTiles.Add(s.TileIndex);
        var scars = new List<ScarState>(history.Scars.Count);
        foreach (var sc in history.Scars) scars.Add(new ScarState(sc.TileIndex, sc.PolityId, sc.Kind, sc.Active));
        Scars = scars;

        var villains = new List<VillainState>(history.Villains.Count);
        foreach (var v in history.Villains) villains.Add(new VillainState(v.Id, v.PolityId, v.FolkKey, v.RisenTurn, v.Agenda));
        Villains = villains;
        Features = context.Features;
        MaxPersons = context.MaxPersons;
        MaxLivingPersonsPerPolity = context.MaxLivingPersonsPerPolity;
        MaxAppointmentsPerTurn = context.MaxAppointmentsPerTurn;
        MaxOutlawBands = context.MaxOutlawBands;
        MaxSiegeMonths = context.MaxSiegeMonths;
        var persons = new List<PersonState>(history.Persons.Count);
        foreach (var p in history.Persons)
        {
            var epithets = new string[p.Epithets.Count];
            for (int i = 0; i < epithets.Length; i++) epithets[i] = history.Strings.NameOf(p.Epithets[i]);
            persons.Add(new PersonState(p.Id, p.Kind, p.FolkKey, p.CultureKey, p.PolityId, p.SeatSettlement, p.HouseId, p.Parent,
                p.Feminine, p.BornTurn, p.BornYear, p.Alive, p.Traits, p.Martial, p.Intrigue, p.Stewardship, p.Charisma, epithets,
                p.Renown, p.Wealth, p.Loyalty, p.Pledged, p.StableKey));
        }
        Persons = persons;
        foreach (var r in history.Relations)
            if (r.Open) _openRelations.Add(new RelationState(r.A, r.B, history.Strings.NameOf(r.Kind), r.FromTurn, r.Weight));
        foreach (var t in history.Tenures)
            if (t.Open) _offices[(t.Polity, history.Strings.NameOf(t.Office))] = t.Person;
        var battles = new List<BattleState>(history.Battles.Count);
        foreach (var b in history.Battles)
            battles.Add(new BattleState(b.Id, b.Turn, b.Kind, b.Outcome, b.Attacker.Polity, b.Defender.Polity, b.Attacker.General,
                b.Defender.General, b.Tile, b.Settlement, b.Parent, b.SiegeEnd >= 0 ? history.Strings.NameOf(b.SiegeEnd) : string.Empty));
        Battles = battles;
        var routes = new List<RouteStateView>(history.RouteStates.Count);
        foreach (var r in history.RouteStates)
            routes.Add(new RouteStateView(r.RouteA, r.RouteB, r.Safety, r.Traffic, r.Forts, r.Waystations, r.OutlawBand, r.Sponsor,
                r.SabotagedTurn, r.LastIncidentTurn));
        RouteStates = routes;
    }

    /// <summary>Risen villains in rise order.</summary>
    public IReadOnlyList<VillainState> Villains { get; }

    /// <summary>The expansion features this run has on.</summary>
    public HistoryFeatures Features { get; }

    /// <summary>True when every flag of <paramref name="features"/> is on.</summary>
    public bool Has(HistoryFeatures features) => (Features & features) == features;

    /// <summary>The person cap over the whole history (<see cref="HistoryContext.MaxPersons"/>).</summary>
    public int MaxPersons { get; }

    /// <summary>The cap on living persons per polity, ruler not counted (<see cref="HistoryContext.MaxLivingPersonsPerPolity"/>).</summary>
    public int MaxLivingPersonsPerPolity { get; }

    /// <summary>The per-turn appointment budget (<see cref="HistoryContext.MaxAppointmentsPerTurn"/>).</summary>
    public int MaxAppointmentsPerTurn { get; }

    /// <summary>The cap on outlaw bands at large (<see cref="HistoryContext.MaxOutlawBands"/>).</summary>
    public int MaxOutlawBands { get; }

    /// <summary>The longest siege (<see cref="HistoryContext.MaxSiegeMonths"/>).</summary>
    public int MaxSiegeMonths { get; }

    /// <summary>Every person (dead included) in id order.</summary>
    public IReadOnlyList<PersonState> Persons { get; }

    /// <summary>Living persons loyal to <paramref name="polityId"/>, in id order.</summary>
    public IReadOnlyList<PersonState> PersonsOf(int polityId)
    {
        if (_personsOf == null)
        {
            _personsOf = new Dictionary<int, List<PersonState>>();
            foreach (var p in Persons)
            {
                if (!p.Alive) continue;
                if (!_personsOf.TryGetValue(p.PolityId, out var list)) _personsOf[p.PolityId] = list = new List<PersonState>();
                list.Add(p);
            }
        }
        return _personsOf.TryGetValue(polityId, out var found) ? found : Array.Empty<PersonState>();
    }

    private Dictionary<int, List<PersonState>>? _personsOf;
    private List<TradeProfile.Route>? _tradeRoutes;

    /// <summary>The turn's routes (built once; callers must not mutate them).</summary>
    internal List<TradeProfile.Route> TradeRoutes => _tradeRoutes ??= TradeProfile.BuildRoutes(this);

    /// <summary>Person id holding <paramref name="office"/> of <paramref name="polityId"/> at the snapshot, or -1.</summary>
    public int OfficeHolder(int polityId, string office)
        => office != null && _offices.TryGetValue((polityId, office), out int person) ? person : -1;

    /// <summary>Open bonds of person <paramref name="personId"/> (either side), in (A, B, Kind, FromTurn) order.</summary>
    public IReadOnlyList<RelationState> RelationsOf(int personId)
    {
        var list = new List<RelationState>();
        foreach (var r in _openRelations) if (r.A == personId || r.B == personId) list.Add(r);
        return list;
    }

    /// <summary>Every engagement in id order.</summary>
    public IReadOnlyList<BattleState> Battles { get; }

    /// <summary>Engagements <paramref name="polityId"/> fought on either side, in id order.</summary>
    public IReadOnlyList<BattleState> BattlesOf(int polityId)
    {
        var list = new List<BattleState>();
        foreach (var b in Battles) if (b.Attacker == polityId || b.Defender == polityId) list.Add(b);
        return list;
    }

    /// <summary>Route states in (RouteA, RouteB) order.</summary>
    public IReadOnlyList<RouteStateView> RouteStates { get; }

    /// <summary>
    /// Reads a plugin state blob as it stood at the snapshot (operators write it through <see cref="ProposalSink.SetState"/>,
    /// which lands at commit, so this is stable for the whole turn).
    /// </summary>
    public PluginStateLookup ReadPluginState(string key, int expectedVersion, out byte[] data)
        => _history.PluginState.TryRead(key, expectedVersion, out data);

    /// <summary>Every scar (ruins, craters, plague pits...) at the snapshot.</summary>
    public IReadOnlyList<ScarState> Scars { get; }

    /// <summary>
    /// The world map. Terrain and capacity layers do not change during history, so reading them keeps the
    /// snapshot semantics; ownership must still be read through <see cref="OwnerOf"/>.
    /// </summary>
    public WorldMap Map => _map ?? throw new InvalidOperationException("This view was built without a map.");

    /// <summary>The turn being simulated; the snapshot is the state at the end of <c>Turn - 1</c>.</summary>
    public int Turn { get; }

    /// <summary>World seed.</summary>
    public int Seed { get; }

    /// <summary>Years per turn.</summary>
    public int TurnYears { get; }

    /// <summary>The polity cap: the commit refuses founding past it.</summary>
    public int MaxPolities { get; }

    /// <summary>The villain cap: the commit refuses a rise past it.</summary>
    public int MaxVillains { get; }

    /// <summary>Cap on collapse successors over the whole history (<see cref="HistoryContext.MaxSuccessors"/>).</summary>
    public int MaxSuccessors { get; }

    /// <summary>Collapse successors founded so far (<see cref="HistoryCatalog.SuccessorCount"/>).</summary>
    internal int SuccessorCount { get; }

    /// <summary>Rebellion headroom above the founding cap (<see cref="HistoryContext.MaxRebellions"/>).</summary>
    public int MaxRebellions { get; }

    /// <summary>Polities that have broken away from a standing parent so far at the snapshot.</summary>
    public int RebelCount { get; }

    /// <summary>Villains risen so far at the snapshot.</summary>
    public int VillainCount { get; }

    /// <summary>The per-polity cap on standing settlements.</summary>
    public int MaxSettlementsPerPolity { get; }

    /// <summary>Multiplier on hazard chances for this run (see <see cref="HistoryContext.HazardRate"/>).</summary>
    public double HazardRate { get; }

    /// <summary>Every settlement (including ended ones) in founding order.</summary>
    public IReadOnlyList<SettlementState> Settlements { get; }

    private double[]? _settlementCapacities;

    /// <summary>
    /// Catchment capacity of standing settlement <paramref name="settlement"/> at the snapshot (0 for an ended or unknown one):
    /// the polity's carrying capacity split among its settlements by the tiles nearest each (see
    /// <see cref="HistoryDemography.SettlementCapacities"/>). Computed once per view.
    /// </summary>
    public double SettlementCapacity(int settlement)
    {
        if ((uint)settlement >= (uint)Settlements.Count) return 0;
        if (_settlementCapacities == null)
        {
            double[] polityCaps = HistoryDemography.CarryingCapacities(Map.DataStore, Polities.Count, _ownership);
            _settlementCapacities = HistoryDemography.SettlementCapacities(Map.DataStore, _ownership, Settlements, polityCaps);
        }
        return _settlementCapacities[settlement];
    }

    /// <summary>Crowding pressure of standing settlement <paramref name="settlement"/>: its people over its catchment capacity (0 when it has none).</summary>
    public double SettlementPressure(int settlement)
        => (uint)settlement < (uint)Settlements.Count
            ? HistoryDemography.PressureOf(Settlements[settlement].Population, SettlementCapacity(settlement))
            : 0;

    /// <summary>People between settlements at the snapshot (released, not yet absorbed).</summary>
    public IReadOnlyList<DisplacedState> DisplacedPool { get; }

    /// <summary>Turns a displaced group waits before the pool settles it by decree.</summary>
    public int DisplacedDecayTurns { get; }

    /// <summary>Declared scalars; <see cref="PolityState.Scalars"/> is indexed by their ids.</summary>
    public ScalarRegistry Scalars { get; }

    /// <summary>Every polity (including ended ones) in id order.</summary>
    public IReadOnlyList<PolityState> Polities { get; }

    /// <summary>Road segments in founding order.</summary>
    public IReadOnlyList<RoadState> Roads { get; }

    /// <summary>Holy sites in founding order.</summary>
    public IReadOnlyList<HolySiteState> HolySites { get; }

    /// <summary>The snapshot ownership array (do not mutate); internal until the plugin API audit.</summary>
    internal int[] OwnershipSnapshot() => _ownership;

    /// <summary>Loads the peoples tables, counting a fallback on the catalog when they fail.</summary>
    internal Peoples.PeoplesStore? LoadPeoples() => PeoplesLoader.LoadStore(_history);

    private BorderIndex? _borders;

    /// <summary>The borders of the snapshot ownership, built on first use (one pass over the tiles).</summary>
    internal BorderIndex Borders => _borders ??= new BorderIndex(Map.DataStore, _ownership);

    private LogisticsGraph? _logistics;
    private WarfareProfile? _logisticsProfile;

    /// <summary>The turn's logistics network under <paramref name="profile"/>, built on first use (rebuilt only for another profile).</summary>
    internal LogisticsGraph Logistics(WarfareProfile profile)
    {
        if (_logistics == null || !ReferenceEquals(_logisticsProfile, profile))
        {
            _logistics = LogisticsGraph.Build(this, profile);
            _logisticsProfile = profile;
        }
        return _logistics;
    }

    /// <summary>Polities bordering <paramref name="polityId"/> at the snapshot, ascending (never <paramref name="polityId"/> itself).</summary>
    public IReadOnlyList<int> BorderingPolities(int polityId) => Borders.Bordering(polityId);

    /// <summary>
    /// Tiles of <paramref name="into"/> touching a tile of <paramref name="from"/> at the snapshot, ascending: the frontier a
    /// force from <paramref name="from"/> crosses. Empty when the two do not border.
    /// </summary>
    public IReadOnlyList<int> FrontierTiles(int from, int into) => Borders.Frontier(from, into);

    /// <summary>Owner polity id of <paramref name="tile"/>, or -1.</summary>
    public int OwnerOf(int tile) => (uint)tile < (uint)_ownership.Length ? _ownership[tile] : -1;

    /// <summary>Number of planet tiles in the ownership map.</summary>
    public int TileCount => _ownership.Length;

    /// <summary>Scalar <paramref name="scalarId"/> of polity <paramref name="polityId"/> at the snapshot, or its default for an unknown polity.</summary>
    public double ScalarOf(int polityId, int scalarId)
    {
        foreach (var p in Polities) if (p.Id == polityId) return p.Scalars[scalarId];
        return Scalars[scalarId].Default;
    }

    /// <summary>True when a historic site (capital, battlefield...) sits on <paramref name="tile"/> at the snapshot.</summary>
    public bool HasSite(int tile) => _siteTiles.Contains(tile);

    /// <summary>Grudge weight between two polities at the snapshot, or 0.</summary>
    public int GrudgeWeight(int a, int b)
        => _grudges.TryGetValue(a < b ? (a, b) : (b, a), out int w) ? w : 0;

    /// <summary>True when a non-aggression window covers this view's turn between the pair.</summary>
    public bool IsWindowed(int a, int b)
    {
        foreach (var (x, y, until) in _windows)
            if (Turn <= until && ((x == a && y == b) || (x == b && y == a))) return true;
        return false;
    }

    /// <summary>Events at <paramref name="tile"/> from earlier turns (log order).</summary>
    public IReadOnlyList<HistoryEvent> EventsAtTile(int tile) => _history.EventsAtTile(tile);

    /// <summary>Events of polity <paramref name="polityId"/> from earlier turns (log order).</summary>
    public IReadOnlyList<HistoryEvent> EventsOf(int polityId) => _history.EventsOf(polityId);
}
