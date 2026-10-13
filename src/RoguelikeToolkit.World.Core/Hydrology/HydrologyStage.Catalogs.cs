using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Features;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Hydrology;

public partial class HydrologyStage
{
    /// <summary>
    /// Builds river and water-body catalogs from the generated fields.
    /// Public so hosts without a <see cref="World"/> (e.g. the visualizer)
    /// can classify water tiles from a bare <see cref="WorldMap"/>.
    /// </summary>
    public static void PopulateCatalogs(WorldMap map, RiverCatalog rivers, WaterBodyCatalog bodies, int maxRivers = 64)
    {
        var store = map.DataStore;
        if (!store.IsLayerRegistered<ElevationInfo>() || !store.IsLayerRegistered<HydrologyInfo>()) return;
        var elev = store.GetSpan<ElevationInfo>();
        var hydro = store.GetSpan<HydrologyInfo>();
        int n = store.TileCount;

        var surface = new float[n];
        var bed = new float[n];
        var depth = new float[n];
        for (int i = 0; i < n; i++)
        {
            surface[i] = hydro[i].Surface;
            bed[i] = elev[i].Height;
            depth[i] = hydro[i].LakeDepth;
        }

        // Group water tiles by body id.
        var groups = new Dictionary<int, WaterBody>();
        for (int i = 0; i < n; i++)
        {
            int id = hydro[i].WaterBodyId;
            if (id < 0) continue;
            if (!groups.TryGetValue(id, out var body))
            {
                body = new WaterBody(id, WaterBodyKind.Lake);
                groups[id] = body;
                bodies.Bodies.Add(body);
            }
            body.Tiles.Add(i);
        }
        // Kinds: above-sea-level groups are always lakes; the largest marine
        // group is the ocean, big marine groups are seas, small ones are lakes
        // (landlocked seas like the Caspian).
        if (bodies.Bodies.Count > 0)
        {
            bodies.Bodies.Sort((a, b) =>
            {
                int c = b.Tiles.Count.CompareTo(a.Tiles.Count);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
            int oceanId = -1;
            int bestMarine = -1;
            foreach (var body in bodies.Bodies)
            {
                bool marine = elev[body.Tiles[0]].Height < ElevationGenerationStage.SeaLevel;
                if (marine && body.Tiles.Count > bestMarine) { bestMarine = body.Tiles.Count; oceanId = body.Id; }
            }
            for (int k = 0; k < bodies.Bodies.Count; k++)
            {
                var old = bodies.Bodies[k];
                bool marine = elev[old.Tiles[0]].Height < ElevationGenerationStage.SeaLevel;
                var kind = !marine ? WaterBodyKind.Lake
                    : old.Id == oceanId ? WaterBodyKind.Ocean
                    : old.Tiles.Count > n / 40 ? WaterBodyKind.Sea : WaterBodyKind.Lake;
                var rebuilt = new WaterBody(old.Id, kind);
                rebuilt.Tiles.AddRange(old.Tiles);
                bodies.Bodies[k] = rebuilt;
            }
        }
        // Boundaries: water tiles adjacent to land (or, for lakes, to dry land).
        Span<int> scratch = stackalloc int[6];
        foreach (var body in bodies.Bodies)
        {
            var inBody = new HashSet<int>(body.Tiles);
            foreach (int t in body.Tiles)
            {
                int adj = store.GetAdjacent(t, scratch);
                for (int k = 0; k < adj; k++)
                {
                    if (!inBody.Contains(scratch[k])) { body.Boundary.Add(t); break; }
                }
            }
        }

        // Rivers: trace downstream from head tiles. A head is a river tile with
        // no upstream river neighbor: higher on the routing surface, or level
        // with it but carrying less water (flat-channel rule, so fill plateaus
        // and lake outlets don't spawn a head per tile).
        // Flat-resolved receivers, shared by head detection and tracing below.
        var drainTo = new int[n];
        Hydrography.ComputeReceivers(store, surface, drainTo);

        var riverTiles = new List<int>();
        for (int i = 0; i < n; i++)
            if (hydro[i].IsRiver == 1) riverTiles.Add(i);
        if (riverTiles.Count == 0) return;

        var riverSet = new HashSet<int>(riverTiles);
        var heads = new List<int>();
        foreach (int t in riverTiles)
        {
            int adj = store.GetAdjacent(t, scratch);
            bool hasUpstreamRiver = false;
            bool hasInboundLink = false;
            for (int k = 0; k < adj; k++)
            {
                int nb = scratch[k];
                if (!riverSet.Contains(nb)) continue;
                if (drainTo[nb] == t) hasInboundLink = true;
                if (surface[nb] > surface[t] + 1e-6f) { hasUpstreamRiver = true; break; }
                if (Math.Abs(surface[nb] - surface[t]) <= 1e-6f && hydro[nb].Flow < hydro[t].Flow)
                {
                    hasUpstreamRiver = true;
                    break;
                }
            }
            // A head starts a new reach: no upstream river neighbor by surface,
            // or no river neighbor draining into it (receiver divergence, e.g.
            // a reach broken mid-course by transmission loss). The latter keeps
            // every IsRiver tile on exactly one cataloged reach.
            if (!hasUpstreamRiver || !hasInboundLink) heads.Add(t);
        }
        // Longest-first: sort heads by flow descending for stable ids.
        var flows = new float[n];
        for (int i = 0; i < n; i++) flows[i] = hydro[i].Flow;
        heads.Sort((a, b) =>
        {
            int c = flows[b].CompareTo(flows[a]);
            return c != 0 ? c : a.CompareTo(b);
        });

        var isRiverTile = new bool[n];
        for (int i = 0; i < n; i++) isRiverTile[i] = hydro[i].IsRiver == 1;
        bool IsTerminalWater(int t) =>
            bed[t] < ElevationGenerationStage.SeaLevel || depth[t] > 0f;
        bool IsRiver(int t) => isRiverTile[t];

        // Receivers recomputed deterministically from the stored surface, so
        // tracing follows the same flat-resolved channels (copied from drainTo).
        var receiver = new int[n];
        drainTo.CopyTo(receiver, 0); // Same routing head detection used; computed once above.

        int riverId = 0;
        var tileOwner = new Dictionary<int, int>();
        foreach (int head in heads)
        {
            if (rivers.Rivers.Count >= maxRivers) break;
            if (tileOwner.ContainsKey(head)) continue;
            var full = Hydrography.TraceRiver(store, receiver, IsRiver, IsTerminalWater, head);
            // Tributaries end at the confluence: trim the path at the first tile
            // already owned by an earlier river, so every reach belongs to exactly
            // one river and tiles resolve unambiguously back to their catalog entry.
            int end = full.Count;
            for (int k = 1; k < full.Count; k++)
            {
                if (tileOwner.ContainsKey(full[k])) { end = k; break; }
            }
            if (end < 2) continue; // Cataloged rivers are multi-tile reaches (valid LINESTRING, Path.Count >= 2); lone trickles stay IsRiver flags only.
            var river = new River(riverId++);
            for (int k = 0; k < end; k++)
            {
                river.Path.Add(full[k]);
                tileOwner[full[k]] = river.Id;
            }
            if (end < full.Count)
            {
                // Joins a cataloged reach downstream: inherit its terminal.
                var downstream = rivers.Rivers[tileOwner[full[end]]];
                river.Terminal = downstream.Terminal;
                river.TerminalBody = downstream.TerminalBody;
            }
            else
            {
                int tail = full[^1];
                if (elev[tail].Height < ElevationGenerationStage.SeaLevel)
                {
                    river.Terminal = RiverTerminal.Sea;
                    river.TerminalBody = hydro[tail].WaterBodyId;
                }
                else if (hydro[tail].LakeDepth > 0f)
                {
                    river.Terminal = RiverTerminal.Lake;
                    river.TerminalBody = hydro[tail].WaterBodyId;
                }
                else
                {
                    river.Terminal = RiverTerminal.Sink;
                    river.TerminalBody = -1;
                }
            }
            for (int k = river.Path.Count - 1; k >= 0; k--)
            {
                if (hydro[river.Path[k]].IsRiver == 1) { river.MouthFlow = hydro[river.Path[k]].Flow; break; }
            }
            rivers.Rivers.Add(river);
        }
    }
}
