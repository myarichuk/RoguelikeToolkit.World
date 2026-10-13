using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Tuning for <see cref="IslandChainDetector"/>.</summary>
public sealed class IslandChainOptions
{
    /// <summary>
    /// A land tile is "thin" when no land tile with coast depth &gt;= this lies within
    /// <see cref="DeepReach"/> rings. Depth 1 is a tile touching water, so 4 means "at
    /// least three tiles from any shore": a ribbon is therefore at most ~6 tiles wide.
    /// </summary>
    public int DeepDepth { get; set; } = 4;

    /// <summary>Rings searched for an interior (deep) tile before a land tile still counts as thin.</summary>
    public int DeepReach { get; set; } = 2;

    /// <summary>Shortest thin run, in rings end to end, that counts as a chain; shorter ones are ordinary islets.</summary>
    public int MinLength { get; set; } = 6;

    /// <summary>
    /// A chain tile must be oceanic crust this many rings or fewer from a plate boundary. Natural thin land
    /// (barrier spits, the coastal strip of a continent) fails it and is never touched.
    /// </summary>
    public int BoundaryRings { get; set; } = 2;

    /// <summary>Bead (surviving island) length range along a broken chain, in rings.</summary>
    public int MinBead { get; set; } = 2;

    /// <summary>Longest surviving bead, in rings.</summary>
    public int MaxBead { get; set; } = 5;

    /// <summary>Strait width range between beads, in rings.</summary>
    public int MinGap { get; set; } = 1;

    /// <summary>Widest strait between beads, in rings.</summary>
    public int MaxGap { get; set; } = 3;

    /// <summary>
    /// Grids with fewer tiles than this are left alone (default: size 5 and up). On a coarse grid a tile is
    /// 400+ km across, so a ribbon one or two tiles wide is already the smallest land feature the grid can
    /// express and cutting it only deletes land (size 3 loses a tenth of it, polar tundra included).
    /// </summary>
    public int MinTileCount { get; set; } = 8000;

    /// <summary>Height a drowned strait tile is pushed down to, below sea level.</summary>
    public double GapDepth { get; set; } = 0.06;
}

/// <summary>
/// A thin, elongated run of land tracing a plate boundary through the open ocean:
/// the unnatural "string of land" an oceanic-oceanic collision paints when its
/// uplift apron is only a tile or two wide but is continuous along the whole border.
/// </summary>
public sealed class IslandChain
{
    internal IslandChain(int anchor, int[] tiles, int[] positions, int length)
    {
        Anchor = anchor;
        TileArray = tiles;
        PositionArray = positions;
        Length = length;
    }

    /// <summary>Tile at one end of the chain (the start of the position axis); stable per chain.</summary>
    public int Anchor { get; }

    /// <summary>The land tiles of the chain.</summary>
    public IReadOnlyList<int> Tiles => TileArray;

    /// <summary>Ring distance of each entry of <see cref="Tiles"/> from <see cref="Anchor"/>.</summary>
    public IReadOnlyList<int> Positions => PositionArray;

    /// <summary>Rings from end to end.</summary>
    public int Length { get; }

    internal int[] TileArray { get; }
    internal int[] PositionArray { get; }
}

/// <summary>
/// Heuristics for the ribbon-of-land artefact along plate boundaries, and the repair
/// that turns a ribbon into a string of discrete islands (the Aleutians, not a wall).
/// Pure functions of the store, deterministic, independent of iteration order.
/// </summary>
/// <remarks>
/// Detection is purely geometric plus one tectonic gate:
/// <list type="number">
/// <item>Coast depth: multi-source BFS from every shore tile inside land.</item>
/// <item>Thin tiles: land with no deep (depth &gt;= 4) tile within two rings, which discards continents,
/// their coasts and any island wider than a few tiles.</item>
/// <item>Tectonic gate, per tile: oceanic crust within a couple of rings of a plate boundary. Natural thin land
/// (barrier spits, coastal strips of a continent) is left alone. It must be applied per tile, before the runs
/// are formed: thin tiles of a whole plate border are one connected network, and a share averaged over it
/// would hide the arcs inside.</item>
/// <item>Chains: connected runs of such tiles at least <see cref="IslandChainOptions.MinLength"/> rings long
/// (two-sweep BFS diameter), so a lone round islet never qualifies.</item>
/// </list>
/// </remarks>
public static class IslandChainDetector
{
    /// <summary>Finds every tectonic ribbon in the current heights. Does not modify anything.</summary>
    public static List<IslandChain> Detect(
        WorldDataStore store, ReadOnlySpan<ElevationInfo> elevation, ReadOnlySpan<TectonicPlate> plates, IslandChainOptions? options = null)
    {
        options ??= new IslandChainOptions();
        int n = store.TileCount;
        var result = new List<IslandChain>();
        if (n == 0 || n < options.MinTileCount) return result;

        Span<int> nb = stackalloc int[6];
        var land = new bool[n];
        for (int i = 0; i < n; i++) land[i] = elevation[i].Height > ElevationGenerationStage.SeaLevel;

        // 1. Coast depth (1 = touches water). Land never reached keeps int.MaxValue: no shore at all means "deep".
        var depth = new int[n];
        Array.Fill(depth, int.MaxValue);
        var queue = new int[n];
        int head = 0, tail = 0;
        for (int i = 0; i < n; i++)
        {
            if (!land[i]) continue;
            int a = store.GetAdjacent(i, nb);
            for (int k = 0; k < a; k++)
                if (!land[nb[k]]) { depth[i] = 1; queue[tail++] = i; break; }
        }
        if (tail == 0) return result; // all land or all water: nothing thin to find
        while (head < tail)
        {
            int cur = queue[head++];
            int a = store.GetAdjacent(cur, nb);
            for (int k = 0; k < a; k++)
            {
                int j = nb[k];
                if (!land[j] || depth[j] != int.MaxValue) continue;
                depth[j] = depth[cur] + 1;
                queue[tail++] = j;
            }
        }

        // 2. Not thin = within DeepReach rings of a deep tile (iterated dilation of the deep set).
        var excluded = new bool[n];
        for (int i = 0; i < n; i++) excluded[i] = land[i] && depth[i] >= options.DeepDepth;
        var scratch = new bool[n];
        for (int pass = 0; pass < Math.Max(0, options.DeepReach); pass++)
        {
            Array.Copy(excluded, scratch, n);
            for (int i = 0; i < n; i++)
            {
                if (excluded[i]) continue;
                int a = store.GetAdjacent(i, nb);
                for (int k = 0; k < a; k++)
                    if (excluded[nb[k]]) { scratch[i] = true; break; }
            }
            (excluded, scratch) = (scratch, excluded);
        }

        // Eligible = thin AND tectonic (see the class remarks). Everything else joins `excluded`.
        for (int i = 0; i < n; i++)
        {
            if (excluded[i] || !land[i]) { excluded[i] = true; continue; }
            var p = plates[i];
            excluded[i] = !(p.Crust == CrustType.Oceanic && p.NearestBoundary != PlateBoundaryType.None && p.BoundaryRings <= options.BoundaryRings);
        }

        // 3. Connected eligible runs, each measured by a two-sweep BFS (diameter in rings).
        var seen = new bool[n];
        var dist = new int[n];
        var members = new List<int>();
        for (int s = 0; s < n; s++)
        {
            if (!land[s] || excluded[s] || seen[s]) continue;
            members.Clear();
            head = tail = 0;
            queue[tail++] = s;
            seen[s] = true;
            while (head < tail)
            {
                int cur = queue[head++];
                members.Add(cur);
                int a = store.GetAdjacent(cur, nb);
                for (int k = 0; k < a; k++)
                {
                    int j = nb[k];
                    if (!land[j] || excluded[j] || seen[j]) continue;
                    seen[j] = true;
                    queue[tail++] = j;
                }
            }
            if (members.Count < options.MinLength) continue;

            int endA = Farthest(store, members, s, excluded, land, dist, queue, nb, out _);
            Farthest(store, members, endA, excluded, land, dist, queue, nb, out int diameter);
            int length = diameter + 1;
            if (length < options.MinLength) continue;

            // dist now holds ring distance from endA for every member.
            var tiles = members.ToArray();
            var positions = new int[tiles.Length];
            for (int m = 0; m < tiles.Length; m++) positions[m] = dist[tiles[m]];
            result.Add(new IslandChain(endA, tiles, positions, length));
        }
        return result;
    }

    /// <summary>Breaks every detected chain into beads: returns the number of strait tiles drowned.</summary>
    /// <remarks>
    /// Walks each chain from its anchor, alternating a bead (kept) of random length and a gap (drowned) of
    /// random length, both drawn from a sub-stream keyed by (<paramref name="seed"/>, anchor tile). Because the
    /// cut is a BFS ring from the chain end it severs the whole ribbon width, however wide or branched.
    /// Tiles the user sculpted (<see cref="UserEditInfo.HeightDelta"/>) or pinned are never drowned.
    /// </remarks>
    public static int Break(
        WorldDataStore store, Span<ElevationInfo> elevation, ReadOnlySpan<TectonicPlate> plates, int seed,
        IslandChainOptions? options = null, ReadOnlySpan<UserEditInfo> edits = default)
    {
        options ??= new IslandChainOptions();
        int drowned = 0;
        float floor = (float)(ElevationGenerationStage.SeaLevel - Math.Max(0.0, options.GapDepth));
        foreach (var chain in Detect(store, elevation, plates, options))
        {
            var rng = Rng.Create(seed + 31337, chain.Anchor);
            var keep = new bool[chain.Length + 1];
            int bead0 = Math.Max(1, options.MinBead), bead1 = Math.Max(bead0, options.MaxBead);
            int gap0 = Math.Max(1, options.MinGap), gap1 = Math.Max(gap0, options.MaxGap);
            int pos = 0;
            while (pos <= chain.Length)
            {
                int bead = bead0 + (int)rng.NextUInt((uint)(bead1 - bead0 + 1));
                for (int b = 0; b < bead && pos + b <= chain.Length; b++) keep[pos + b] = true;
                pos += bead + gap0 + (int)rng.NextUInt((uint)(gap1 - gap0 + 1));
            }
            var tiles = chain.TileArray;
            var positions = chain.PositionArray;
            for (int m = 0; m < tiles.Length; m++)
            {
                if (keep[positions[m]]) continue;
                if (!edits.IsEmpty && IsHandEdited(edits[tiles[m]])) continue;
                ref var e = ref elevation[tiles[m]];
                if (e.Height > floor) { e.Height = floor; drowned++; }
            }
        }
        return drowned;
    }

    private static bool IsHandEdited(in UserEditInfo e)
        => e.HeightDelta != 0f || (e.Flags & UserEditFlags.PinHeight) != 0;

    /// <summary>BFS over the thin run from <paramref name="from"/>; fills <paramref name="dist"/> and returns the farthest tile.</summary>
    private static int Farthest(
        WorldDataStore store, List<int> members, int from, bool[] excluded, bool[] land,
        int[] dist, int[] queue, Span<int> nb, out int maxDist)
    {
        foreach (int t in members) dist[t] = -1;
        int head = 0, tail = 0;
        queue[tail++] = from;
        dist[from] = 0;
        int far = from;
        while (head < tail)
        {
            int cur = queue[head++];
            if (dist[cur] > dist[far]) far = cur;
            int a = store.GetAdjacent(cur, nb);
            for (int k = 0; k < a; k++)
            {
                int j = nb[k];
                if (!land[j] || excluded[j] || dist[j] != -1) continue;
                dist[j] = dist[cur] + 1;
                queue[tail++] = j;
            }
        }
        maxDist = dist[far];
        return far;
    }
}
