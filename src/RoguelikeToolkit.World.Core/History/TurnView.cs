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

    /// <summary>One holy site at the snapshot.</summary>
    public readonly record struct HolySiteState(int Tile, int PolityId, GeoCoord Position, bool Active);

    /// <summary>One settlement at the snapshot; <see cref="Index"/> is its position in <see cref="HistoryCatalog.Settlements"/>.</summary>
    public readonly record struct SettlementState(int Index, int Tile, int PolityId, SettlementTier Tier, SettlementRole Role, int Parent, int FoundedTurn, bool Ended, double Population = 0);

    /// <summary>One displaced group at the snapshot; <see cref="Index"/> is its position in <see cref="HistoryCatalog.Displaced"/>.</summary>
    public readonly record struct DisplacedState(int Index, int PolityId, string Culture, int Turn, double Amount, int Origin, int Cause);

    private readonly HashSet<int> _siteTiles = new();

    internal TurnView(WorldMap? map, HistoryCatalog history, HistoryContext context, int turn)
    {
        _map = map;
        _history = history;
        Turn = turn;
        Seed = history.Seed;
        TurnYears = context.TurnYears;
        MaxPolities = context.MaxPolities;
        MaxVillains = context.MaxVillains;
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
        foreach (var s in history.Sites) _siteTiles.Add(s.TileIndex);
    }

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

    /// <summary>Villains risen so far at the snapshot.</summary>
    public int VillainCount { get; }

    /// <summary>The per-polity cap on standing settlements.</summary>
    public int MaxSettlementsPerPolity { get; }

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
