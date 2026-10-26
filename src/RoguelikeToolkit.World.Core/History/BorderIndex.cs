using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Every border of one ownership snapshot, from a single O(tiles·6) pass: which polities touch which, and the tiles of each
/// pair's frontier. It answers what <see cref="HistoryNeighbors.Bordering"/> answers with one scan per call, so a turn
/// that asks it once per polity per operator no longer pays tiles × polities × operators.
/// </summary>
internal sealed class BorderIndex
{
    private static readonly int[] None = Array.Empty<int>();
    private readonly Dictionary<int, int[]> _bordering = new();
    private readonly Dictionary<(int From, int Into), List<int>> _frontier = new();

    /// <summary>Indexes <paramref name="ownership"/> over <paramref name="store"/>'s adjacency.</summary>
    public BorderIndex(WorldDataStore store, int[] ownership)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ownership);
        var sets = new Dictionary<int, SortedSet<int>>();
        Span<int> neighbors = stackalloc int[6];
        int tiles = Math.Min(store.TileCount, ownership.Length);
        for (int tile = 0; tile < tiles; tile++)
        {
            int owner = ownership[tile];
            if (owner < 0) continue;
            int adjacent = store.GetAdjacent(tile, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                int other = (uint)neighbors[k] < (uint)ownership.Length ? ownership[neighbors[k]] : -1;
                if (other < 0 || other == owner) continue;
                if (!sets.TryGetValue(owner, out var set)) sets[owner] = set = new SortedSet<int>();
                set.Add(other);
                // Tiles ascend, so a pair's frontier list is ascending and a repeat can only be its last entry.
                if (!_frontier.TryGetValue((other, owner), out var list)) _frontier[(other, owner)] = list = new List<int>();
                if (list.Count == 0 || list[list.Count - 1] != tile) list.Add(tile);
            }
        }
        foreach (var (polity, set) in sets) _bordering[polity] = set.ToArray();
    }

    /// <summary>Polities owning a tile adjacent to <paramref name="polity"/>'s land, ascending; never <paramref name="polity"/> itself.</summary>
    public IReadOnlyList<int> Bordering(int polity) => _bordering.TryGetValue(polity, out var list) ? list : None;

    /// <summary>
    /// Tiles of <paramref name="into"/> that touch a tile of <paramref name="from"/>, ascending: the frontier a force from
    /// <paramref name="from"/> crosses into <paramref name="into"/>. Empty when they do not border.
    /// </summary>
    public IReadOnlyList<int> Frontier(int from, int into) => _frontier.TryGetValue((from, into), out var list) ? list : None;
}
