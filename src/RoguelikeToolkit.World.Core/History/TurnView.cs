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
        bool Alive, int Parent, double Population, IReadOnlyList<double> Scalars, string CultureOverlay = "");

    /// <summary>One road segment at the snapshot.</summary>
    public readonly record struct RoadState(int TileA, int TileB, int PolityId, int RouteA, int RouteB, bool Severed);

    /// <summary>One holy site at the snapshot.</summary>
    public readonly record struct HolySiteState(int Tile, int PolityId, GeoCoord Position, bool Active);

    private readonly HashSet<int> _siteTiles = new();

    internal TurnView(WorldMap? map, HistoryCatalog history, HistoryContext context, int turn)
    {
        _map = map;
        _history = history;
        Turn = turn;
        Seed = history.Seed;
        TurnYears = context.TurnYears;
        Scalars = history.Scalars;
        _ownership = (int[])history.Ownership.Clone();
        var polities = new List<PolityState>(history.Polities.Count);
        foreach (var p in history.Polities)
        {
            var scalars = new double[history.Scalars.Count];
            for (int i = 0; i < scalars.Length; i++) scalars[i] = p.GetScalar(i, history.Scalars);
            polities.Add(new PolityState(p.Id, p.Form, p.CapitalTile, p.FolkKey, p.CultureKey, p.Alive, p.Parent, p.Population, scalars, p.CultureOverlay ?? string.Empty));
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
