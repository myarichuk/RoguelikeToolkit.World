using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 30: opens trade routes between polity capitals. Every turn, in deterministic pair
/// order, surplus-complementary pairs (grain land vs herd/timber land) within trade range roll
/// to connect: the route follows the cheapest land path preferring river, coastal and low-danger
/// steps, and is stored as one <see cref="HistoryRoad"/> segment per step. Routes cost one
/// emergence each; war severs them (<see cref="WarOperator"/>) and exchange diffuses along the
/// survivors (<see cref="ExchangeOperator"/>). Re-running is idempotent: connected pairs are skipped
/// and every draw is a pure function of <c>(seed, turn, pair)</c>.
/// </summary>
public sealed class TradeRoadOperator : IHistoryOperator
{
    /// <summary>Per-pair, per-turn chance to open a route once everything else lines up.</summary>
    public const double RoadChance = 0.15;

    /// <summary>Longest route in tiles; farther capitals stay unconnected.</summary>
    public const int MaxRouteTiles = 600;

    /// <summary>Trade range in degrees of arc between capitals.</summary>
    public const double TradeRangeDegrees = 30.0;

    /// <summary>Per-settlement, per-turn chance to build a spur to its own polity's network.</summary>
    public const double SpurChance = 0.25;

    /// <summary>Longest spur in degrees of arc between the two settlements.</summary>
    public const double SpurRangeDegrees = 12.0;

    /// <summary>Diagnostic name, <c>TradeRoad</c>.</summary>
    public string Name => "TradeRoad";

    /// <summary><see cref="StageOrder.TradeRoad"/> (30).</summary>
    public int Order => StageOrder.TradeRoad;

    /// <summary>Proposes this turn's new routes from the snapshot.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        var polities = view.Polities;
        var store = view.Map.DataStore;
        var profiles = SurplusProfiles(store, view);
        double rangeDot = DetMath.Cos(TradeRangeDegrees * GeoCoord.Deg2Rad);
        var vectors = store.GetTileVectors();
        var routes = new HashSet<(int, int)>();
        foreach (var road in view.Roads) routes.Add((road.RouteA, road.RouteB));
        if (polities.Count >= 2) ProposeTrunks(view, sink, store, polities, profiles, vectors, rangeDot, routes);
        ProposeSpurs(view, sink, store, vectors, routes);
    }

    private static void ProposeTrunks(
        TurnView view, ProposalSink sink, WorldDataStore store, IReadOnlyList<TurnView.PolityState> polities,
        int[][] profiles, ReadOnlySpan<Vector3D> vectors, double rangeDot, HashSet<(int, int)> routes)
    {
        for (int a = 0; a < polities.Count; a++)
        {
            var pa = polities[a];
            if (!pa.Alive) continue;
            for (int b = a + 1; b < polities.Count; b++)
            {
                var pb = polities[b];
                if (!pb.Alive) continue;
                int endA = Math.Min(pa.CapitalTile, pb.CapitalTile);
                int endB = Math.Max(pa.CapitalTile, pb.CapitalTile);
                if (routes.Contains((endA, endB))) continue;
                if (!HistoryDemography.AreComplementary(profiles[a], profiles[b])) continue;
                if (Vector3D.Dot(vectors[pa.CapitalTile], vectors[pb.CapitalTile]) < rangeDot) continue;
                var stream = sink.Rng(a * 100003 + b, "open");
                if (stream.NextDouble() >= RoadChance) continue;
                List<int>? path = FindRoute(store, pa.CapitalTile, pb.CapitalTile);
                if (path is null) continue;
                if (!sink.TrySpendEmergence()) continue;
                for (int i = 0; i + 1 < path.Count; i++)
                    sink.Emit(new Proposal.AddRoad { EntityId = -1, TileA = path[i], TileB = path[i + 1], RouteA = endA, RouteB = endB });
                sink.Event(HistoryEventKind.RoadBuilt, a, pa.CapitalTile,
                    new[] { "road", "built", HistoryDemography.RouteTag(endA, endB) });
                routes.Add((endA, endB));
            }
        }
    }

    /// <summary>
    /// A standing settlement with no route yet roads itself to its parent, else its nearest
    /// standing neighbour of the same polity, so the polity's settlements form one network the
    /// capitals' trunk routes plug into. Spurs belong to the polity (<c>PolityId</c> &gt;= 0).
    /// </summary>
    private static void ProposeSpurs(
        TurnView view, ProposalSink sink, WorldDataStore store, ReadOnlySpan<Vector3D> vectors, HashSet<(int, int)> routes)
    {
        double rangeDot = DetMath.Cos(SpurRangeDegrees * GeoCoord.Deg2Rad);
        var linked = new HashSet<int>();
        foreach (var (a, b) in routes) { linked.Add(a); linked.Add(b); }
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId < 0 || linked.Contains(s.Tile)) continue;
            var stream = sink.Rng(1_000_000 + s.Index, "spur");
            if (stream.NextDouble() >= SpurChance) continue;
            int target = -1;
            if (s.Parent >= 0 && s.Parent < view.Settlements.Count)
            {
                var parent = view.Settlements[s.Parent];
                if (!parent.Ended && parent.PolityId == s.PolityId && parent.Tile != s.Tile
                    && Vector3D.Dot(vectors[s.Tile], vectors[parent.Tile]) >= rangeDot) target = parent.Tile;
            }
            if (target < 0)
            {
                double best = rangeDot;
                foreach (var o in view.Settlements)
                {
                    if (o.Ended || o.PolityId != s.PolityId || o.Index == s.Index || o.Tile == s.Tile) continue;
                    double dot = Vector3D.Dot(vectors[s.Tile], vectors[o.Tile]);
                    if (dot > best) { best = dot; target = o.Tile; }
                }
            }
            if (target < 0) continue;
            List<int>? path = FindRoute(store, s.Tile, target);
            if (path is null) continue;
            if (!sink.TrySpendEmergence()) return;
            int endA = Math.Min(s.Tile, target);
            int endB = Math.Max(s.Tile, target);
            for (int i = 0; i + 1 < path.Count; i++)
                sink.Emit(new Proposal.AddRoad { EntityId = s.PolityId, TileA = path[i], TileB = path[i + 1], RouteA = endA, RouteB = endB });
            sink.Event(HistoryEventKind.RoadBuilt, s.PolityId, s.Tile,
                new[] { "road", "built", "spur", HistoryDemography.RouteTag(endA, endB) });
            routes.Add((endA, endB));
            linked.Add(s.Tile);
            linked.Add(target);
        }
    }

    private static int[][] SurplusProfiles(
        WorldDataStore store, TurnView view)
    {
        var profiles = new int[view.Polities.Count][];
        for (int i = 0; i < profiles.Length; i++) profiles[i] = new int[4];
        bool hasCapacity = store.IsLayerRegistered<LandCapacityInfo>();
        var capacity = hasCapacity ? store.GetSpan<LandCapacityInfo>() : default;
        bool hasBiome = store.IsLayerRegistered<BiomeInfo>();
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;
        for (int tile = 0; tile < store.TileCount; tile++)
        {
            int owner = view.OwnerOf(tile);
            if ((uint)owner >= (uint)profiles.Length) continue;
            int[] profile;
            if (hasCapacity)
            {
                var cell = capacity[tile];
                profile = new[] { (int)cell.Arable, (int)cell.Pasture, (int)cell.Game, (int)cell.Timber };
            }
            else if (hasBiome)
            {
                profile = HistoryDemography.BiomeSurplus(biomes[tile].Biome);
            }
            else
            {
                continue;
            }
            var current = profiles[owner];
            for (int k = 0; k < 4; k++) current[k] += profile[k];
        }
        return profiles;
    }

    private static List<int>? FindRoute(WorldDataStore store, int from, int to)
    {
        int n = store.TileCount;
        if ((uint)from >= (uint)n || (uint)to >= (uint)n || from == to) return null;
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        bool hasHydro = store.IsLayerRegistered<HydrologyInfo>();
        var hydro = hasHydro ? store.GetSpan<HydrologyInfo>() : default;
        bool hasBiome = store.IsLayerRegistered<BiomeInfo>();
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;

        var dist = new int[n];
        var prev = new int[n];
        for (int i = 0; i < n; i++) { dist[i] = -1; prev[i] = -1; }
        var open = new SortedSet<(int Cost, int Tile, long Seq)>();
        long seq = 0;
        dist[from] = 0;
        open.Add((0, from, seq++));
        Span<int> neighbors = stackalloc int[6];

        while (open.Count > 0)
        {
            var current = open.Min;
            open.Remove(current);
            if (current.Cost != dist[current.Tile]) continue;
            if (current.Tile == to) break;
            int adjacent = store.GetAdjacent(current.Tile, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                int next = neighbors[k];
                if (dist[next] >= 0 && dist[next] <= current.Cost + 1) continue;
                if (hasElev && elev[next].Height < HistoryTerritory.LandThreshold) continue;
                int step = 10;
                if (hasBiome) step += biomes[next].DangerLevel * 4;
                if (hasHydro && hydro[next].IsRiver != 0) step -= 3;
                if (hasElev && IsCoastal(store, elev, neighbors, next)) step -= 2;
                if (step < 1) step = 1;
                int cost = current.Cost + step;
                if (dist[next] < 0 || cost < dist[next])
                {
                    dist[next] = cost;
                    prev[next] = current.Tile;
                    open.Add((cost, next, seq++));
                }
            }
        }

        if (dist[to] < 0) return null;
        var tiles = new List<int>();
        for (int tile = to; tile != -1; tile = prev[tile]) tiles.Add(tile);
        tiles.Reverse();
        if (tiles.Count > MaxRouteTiles + 1) return null;
        return tiles;
    }

    private static bool IsCoastal(WorldDataStore store, Span<ElevationInfo> elev, Span<int> neighbors, int tile)
    {
        int adjacent = store.GetAdjacent(tile, neighbors);
        for (int k = 0; k < adjacent; k++)
            if (elev[neighbors[k]].Height < HistoryTerritory.LandThreshold) return true;
        return false;
    }
}
