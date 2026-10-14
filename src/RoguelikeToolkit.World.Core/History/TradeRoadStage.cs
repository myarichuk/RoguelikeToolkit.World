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
/// emergence each; war severs them (<see cref="WarStage"/>) and exchange diffuses along the
/// survivors (<see cref="ExchangeStage"/>). Re-running is idempotent: connected pairs are skipped
/// and every draw is a pure function of <c>(seed, turn, pair)</c>.
/// </summary>
public sealed class TradeRoadStage : HistoryPluginBase
{
    /// <summary>Per-pair, per-turn chance to open a route once everything else lines up.</summary>
    public const double RoadChance = 0.15;

    /// <summary>Longest route in tiles; farther capitals stay unconnected.</summary>
    public const int MaxRouteTiles = 600;

    /// <summary>Trade range in degrees of arc between capitals.</summary>
    public const double TradeRangeDegrees = 30.0;

    /// <summary>Diagnostic name, <c>TradeRoad</c>.</summary>
    public override string Name => "TradeRoad";
    /// <summary><see cref="StageOrder.TradeRoad"/> (30).</summary>
    public override int Order => StageOrder.TradeRoad;
    /// <summary>Elevation (the land mask and the coastal bonus).</summary>
    public override Type[] Reads => new[] { typeof(ElevationInfo) };
    /// <summary>Hydrology (rivers), biomes (danger) and land capacity (surplus); absent layers fall back to neutral values.</summary>
    public override Type[] ReadsOptional => new[] { typeof(HydrologyInfo), typeof(BiomeInfo), typeof(LandCapacityInfo) };

    /// <summary>True once polities have formed and at least two could trade.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history)
        => history.PolitiesFormed && history.Polities.Count >= 2;

    /// <summary>Opens routes turn by turn over the whole span.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ResetOperatorBudget(Name);
        var store = map.DataStore;
        var profiles = SurplusProfiles(store, history);
        double rangeDot = DetMath.Cos(TradeRangeDegrees * GeoCoord.Deg2Rad);
        var vectors = store.GetTileVectors();

        for (int turn = 1; turn < context.TurnCount; turn++)
        {
            for (int a = 0; a < history.Polities.Count; a++)
            {
                for (int b = a + 1; b < history.Polities.Count; b++)
                {
                    var pa = history.Polities[a];
                    var pb = history.Polities[b];
                    int endA = Math.Min(pa.CapitalTile, pb.CapitalTile);
                    int endB = Math.Max(pa.CapitalTile, pb.CapitalTile);
                    if (RouteExists(history, endA, endB)) continue;
                    if (!HistoryDemography.AreComplementary(profiles[a], profiles[b])) continue;
                    if (Vector3D.Dot(vectors[pa.CapitalTile], vectors[pb.CapitalTile]) < rangeDot) continue;
                    var stream = HistoryRng.Create(history.Seed, Name, turn, a * 100003 + b, "open");
                    if (stream.NextDouble() >= RoadChance) continue;
                    List<int>? path = FindRoute(store, history, pa.CapitalTile, pb.CapitalTile);
                    if (path is null) continue;
                    if (!context.TrySpendOperatorEmergence(Name, turn)) continue;
                    for (int i = 0; i + 1 < path.Count; i++)
                    {
                        history.Roads.Add(new HistoryRoad
                        {
                            TileA = path[i],
                            TileB = path[i + 1],
                            PolityId = -1,
                            RouteA = endA,
                            RouteB = endB,
                            FoundedTurn = turn,
                        });
                    }
                    history.AppendEvent(HistoryEventKind.RoadBuilt, turn, a, pa.CapitalTile,
                        new[] { "road", "built", HistoryDemography.RouteTag(endA, endB) });
                }
            }
        }
    }

    private static bool RouteExists(HistoryCatalog history, int endA, int endB)
    {
        foreach (var road in history.Roads)
            if (road.RouteA == endA && road.RouteB == endB) return true;
        return false;
    }

    private static int[][] SurplusProfiles(
        WorldDataStore store, HistoryCatalog history)
    {
        var profiles = new int[history.Polities.Count][];
        for (int i = 0; i < profiles.Length; i++) profiles[i] = new int[4];
        bool hasCapacity = store.IsLayerRegistered<LandCapacityInfo>();
        var capacity = hasCapacity ? store.GetSpan<LandCapacityInfo>() : default;
        bool hasBiome = store.IsLayerRegistered<BiomeInfo>();
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;
        for (int tile = 0; tile < store.TileCount; tile++)
        {
            int owner = history.OwnerOf(tile);
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

    private static List<int>? FindRoute(WorldDataStore store, HistoryCatalog history, int from, int to)
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
