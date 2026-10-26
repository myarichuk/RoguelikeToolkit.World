using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// The logistics network of one turn (docs/HISTORY_EXPANSION_PLAN.md §4): standing settlements are the nodes, linked by the
/// roads between capitals, by rivers, by sea lanes and across open country. Costs are road-degrees
/// (<see cref="Peoples.LogisticsDef"/>). Built once per <see cref="TurnView"/> and profile; what a link costs depends on who
/// moves (<see cref="CostsFrom"/>): only a seafaring mover sails, a third party's land costs more, and the land of a polity the
/// mover fought within <see cref="Peoples.LogisticsDef.WarTurns"/> costs more still. A host's target is exempt from both, its
/// land is where the host is going.
/// </summary>
internal sealed class LogisticsGraph
{
    /// <summary>What a link runs along.</summary>
    internal enum LinkKind : byte { Road, River, Sea, Overland }

    /// <summary>One directed link: target node, base cost, kind.</summary>
    internal readonly record struct Link(int To, double Cost, LinkKind Kind);

    private readonly WarfareProfile _profile;
    private readonly TurnView _view;
    private readonly int[] _nodeTile, _nodeOwner, _nodeSettlement;
    private readonly double[] _nodeSource;
    private readonly int[] _nodeTerrain;
    private readonly List<Link>[] _links;
    private readonly HashSet<(int, int)> _hostile = new();
    private readonly Dictionary<(int Mover, int Target, int Exclude), double[]> _costs = new();

    private LogisticsGraph(TurnView view, WarfareProfile profile, int nodes)
    {
        _view = view;
        _profile = profile;
        _nodeTile = new int[nodes];
        _nodeOwner = new int[nodes];
        _nodeSettlement = new int[nodes];
        _nodeSource = new double[nodes];
        _nodeTerrain = new int[nodes];
        _links = new List<Link>[nodes];
        for (int i = 0; i < nodes; i++) _links[i] = new List<Link>();
    }

    /// <summary>Nodes: the standing settlements, in index order.</summary>
    public int NodeCount => _nodeTile.Length;

    /// <summary>Tile of node <paramref name="node"/>.</summary>
    public int TileOf(int node) => _nodeTile[node];

    /// <summary>Settlement index of node <paramref name="node"/>.</summary>
    public int SettlementOf(int node) => _nodeSettlement[node];

    /// <summary>Owner of node <paramref name="node"/>.</summary>
    public int OwnerOf(int node) => _nodeOwner[node];

    /// <summary>Links out of node <paramref name="node"/>.</summary>
    public IReadOnlyList<Link> LinksOf(int node) => _links[node];

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> fought within the data's war window.</summary>
    public bool AtWar(int a, int b) => _hostile.Contains(a < b ? (a, b) : (b, a));

    internal static double Degrees(Vector3D a, Vector3D b) => DetMath.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(a, b)))) * (180 / Math.PI);

    /// <summary>Builds the network of <paramref name="view"/>.</summary>
    public static LogisticsGraph Build(TurnView view, WarfareProfile profile)
    {
        var data = profile.Logistics;
        var standing = new List<TurnView.SettlementState>();
        foreach (var s in view.Settlements) if (!s.Ended) standing.Add(s);
        var g = new LogisticsGraph(view, profile, standing.Count);
        var store = view.Map.DataStore;
        var vectors = store.GetTileVectors();
        bool hasElev = store.IsLayerRegistered<ElevationInfo>(), hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        bool hasBiome = store.IsLayerRegistered<BiomeInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;
        var river = new bool[standing.Count];
        var coastal = new bool[standing.Count];
        var byTile = new Dictionary<int, int>();
        Span<int> neighbors = stackalloc int[6];
        for (int i = 0; i < standing.Count; i++)
        {
            var s = standing[i];
            g._nodeTile[i] = s.Tile;
            g._nodeOwner[i] = s.PolityId;
            g._nodeSettlement[i] = s.Index;
            g._nodeSource[i] = s.Role == SettlementRole.Waystation ? data.WaystationSource : profile.SourceCost(s.Tier);
            g._nodeTerrain[i] = g.TerrainAt(s.Tile, hasBiome, biomes, hasElev, elev);
            river[i] = hasHydro && hydro[s.Tile].IsRiver != 0;
            if (hasElev)
            {
                int n = store.GetAdjacent(s.Tile, neighbors);
                for (int k = 0; k < n; k++)
                    if ((uint)neighbors[k] < (uint)elev.Length && elev[neighbors[k]].Height < 0) { coastal[i] = true; break; }
            }
            if (!byTile.ContainsKey(s.Tile)) byTile[s.Tile] = i;
        }

        // Battles within the war window make their two sides hostile to each other.
        foreach (var b in view.Battles)
            if (b.Turn >= view.Turn - data.WarTurns && b.Attacker >= 0 && b.Defender >= 0 && b.Attacker != b.Defender)
                g._hostile.Add(b.Attacker < b.Defender ? (b.Attacker, b.Defender) : (b.Defender, b.Attacker));

        // Pairwise links: rivers, sea lanes, open country.
        double reach = Math.Max(data.OverlandDegrees, Math.Max(data.RiverDegrees, data.SeaDegrees));
        for (int i = 0; i < standing.Count; i++)
            for (int j = i + 1; j < standing.Count; j++)
            {
                double d = Degrees(vectors[g._nodeTile[i]], vectors[g._nodeTile[j]]);
                if (d > reach) continue;
                if (d <= data.OverlandDegrees)
                    g.Both(i, j, d * data.OverlandCost * 0.5 * (profile.TerrainCost[g._nodeTerrain[i]] + profile.TerrainCost[g._nodeTerrain[j]]), LinkKind.Overland);
                if (river[i] && river[j] && d <= data.RiverDegrees) g.Both(i, j, d * data.RiverCost, LinkKind.River);
                if (coastal[i] && coastal[j] && d <= data.SeaDegrees) g.Both(i, j, d * data.SeaCost, LinkKind.Sea);
            }

        // Roads: every route between two standing capitals, its length the sum of its segments.
        var routes = new SortedDictionary<(int A, int B), (double Length, bool Severed)>();
        foreach (var r in view.Roads)
        {
            var key = (r.RouteA, r.RouteB);
            routes.TryGetValue(key, out var route);
            routes[key] = (route.Length + Degrees(vectors[r.TileA], vectors[r.TileB]), route.Severed || r.Severed);
        }
        var states = new Dictionary<(int, int), TurnView.RouteStateView>();
        foreach (var st in view.RouteStates) states[(st.RouteA, st.RouteB)] = st;
        foreach (var (key, route) in routes)
        {
            if (!byTile.TryGetValue(key.A, out int a) || !byTile.TryGetValue(key.B, out int b) || a == b) continue;
            double safety = HistoryRouteState.NewRouteSafety;
            int ways = 0;
            bool sabotaged = false;
            if (states.TryGetValue(key, out var state))
            {
                safety = state.Safety;
                ways = state.Waystations;
                sabotaged = state.SabotagedTurn >= 0 && state.SabotagedTurn >= view.Turn - data.SabotagedTurns;
            }
            double cost = route.Length * data.RoadCost * (1 + data.RoadUnsafe * (1 - safety))
                * (route.Severed || sabotaged ? data.SeveredCost : 1) * Math.Max(0, 1 - data.WaystationRelief * Math.Min(ways, Math.Max(0, data.WaystationsCounted)));
            g.Both(a, b, cost, LinkKind.Road);
        }
        return g;
    }

    private int TerrainAt(int tile, bool hasBiome, ReadOnlySpan<BiomeInfo> biomes, bool hasElev, ReadOnlySpan<ElevationInfo> elev)
        => hasBiome ? _profile.TerrainOf(biomes[tile].Biome, hasElev ? elev[tile].Height : 0) : _profile.DefaultTerrain;

    private void Both(int a, int b, double cost, LinkKind kind)
    {
        _links[a].Add(new Link(b, cost, kind));
        _links[b].Add(new Link(a, cost, kind));
    }

    /// <summary>What crossing into land held by <paramref name="owner"/> costs <paramref name="mover"/> (heading for <paramref name="target"/>).</summary>
    private double Passage(int mover, int target, int owner)
    {
        if (owner < 0 || owner == mover || owner == target) return 1;
        return AtWar(mover, owner) ? _profile.Logistics.HostileCost : _profile.Logistics.ForeignCost;
    }

    /// <summary>
    /// Network cost from <paramref name="mover"/>'s settlements (each seeded with its source cost) to every node, for a host or
    /// caravan heading for <paramref name="target"/> (-1: none), leaving out settlement <paramref name="exclude"/> (a besieged place
    /// feeds no relief). Unreachable nodes are +∞. Cached.
    /// </summary>
    public double[] CostsFrom(int mover, int target = -1, int exclude = -1)
    {
        if (_costs.TryGetValue((mover, target, exclude), out var cached)) return cached;
        int n = NodeCount;
        var cost = new double[n];
        for (int i = 0; i < n; i++) cost[i] = double.PositiveInfinity;
        var open = new SortedSet<(double Cost, int Node)>();
        for (int i = 0; i < n; i++)
            if (_nodeOwner[i] == mover && _nodeSettlement[i] != exclude)
            {
                cost[i] = Math.Max(0, _nodeSource[i]);
                open.Add((cost[i], i));
            }
        bool sails = false;
        if ((uint)mover < (uint)_view.Polities.Count)
        {
            var polity = _view.Polities[mover];
            sails = _profile.Sails(polity.Form, _profile.WayOf(polity.CultureKey));
        }
        while (open.Count > 0)
        {
            var (c, u) = open.Min;
            open.Remove(open.Min);
            if (c > cost[u]) continue;
            foreach (var link in _links[u])
            {
                if (_nodeSettlement[link.To] == exclude) continue;
                if (link.Kind == LinkKind.Sea && !sails) continue;
                double step = link.Cost * Math.Max(Passage(mover, target, _nodeOwner[u]), Passage(mover, target, _nodeOwner[link.To]));
                double next = c + step;
                if (next < cost[link.To])
                {
                    if (!double.IsPositiveInfinity(cost[link.To])) open.Remove((cost[link.To], link.To));
                    cost[link.To] = next;
                    open.Add((next, link.To));
                }
            }
        }
        _costs[(mover, target, exclude)] = cost;
        return cost;
    }

    /// <summary>
    /// Network cost from <paramref name="mover"/> to <paramref name="tile"/>: the cheapest node it reaches, plus the last leg across
    /// open country (its ground, the land's holder). +∞ when the mover holds nothing.
    /// </summary>
    public double CostTo(int mover, int tile, int target = -1, int exclude = -1) => CostTo(mover, tile, target, exclude, out _);

    /// <summary>As <see cref="CostTo(int, int, int, int)"/>, with the node the last leg leaves from (-1 when unreachable).</summary>
    public double CostTo(int mover, int tile, int target, int exclude, out int from)
    {
        var costs = CostsFrom(mover, target, exclude);
        var store = _view.Map.DataStore;
        var vectors = store.GetTileVectors();
        bool hasElev = store.IsLayerRegistered<ElevationInfo>(), hasBiome = store.IsLayerRegistered<BiomeInfo>();
        int terrain = TerrainAt(tile, hasBiome, hasBiome ? store.GetSpan<BiomeInfo>() : default, hasElev, hasElev ? store.GetSpan<ElevationInfo>() : default);
        double passage = Passage(mover, target, _view.OwnerOf(tile));
        double best = double.PositiveInfinity;
        from = -1;
        for (int i = 0; i < NodeCount; i++)
        {
            if (double.IsPositiveInfinity(costs[i])) continue;
            double leg = Degrees(vectors[_nodeTile[i]], vectors[tile]) * _profile.Logistics.OverlandCost
                * 0.5 * (_profile.TerrainCost[_nodeTerrain[i]] + _profile.TerrainCost[terrain]) * passage;
            if (costs[i] + leg < best) { best = costs[i] + leg; from = i; }
        }
        return best;
    }
}
