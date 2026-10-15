using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One deterministic neighbor lookup for the conflict operators: the polity ids owning a tile
/// adjacent to <paramref name="polityId"/>'s territory, ascending. Shared by <see cref="WarOperator"/>
/// and <see cref="KhanOperator"/> so both read the same border.
/// </summary>
public static class HistoryNeighbors
{
    /// <summary>Every polity bordering <paramref name="polityId"/>, ascending (never includes <paramref name="polityId"/> itself).</summary>
    public static List<int> Bordering(WorldDataStore store, int[] ownership, int polityId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ownership);
        var bordering = new List<int>();
        Span<int> neighbors = stackalloc int[6];
        int tiles = Math.Min(store.TileCount, ownership.Length);
        for (int tile = 0; tile < tiles; tile++)
        {
            if (ownership[tile] != polityId) continue;
            int adjacent = store.GetAdjacent(tile, neighbors);
            for (int k = 0; k < adjacent; k++)
            {
                int owner = (uint)neighbors[k] < (uint)ownership.Length ? ownership[neighbors[k]] : -1;
                if (owner >= 0 && owner != polityId && !bordering.Contains(owner)) bordering.Add(owner);
            }
        }
        bordering.Sort();
        return bordering;
    }
}
