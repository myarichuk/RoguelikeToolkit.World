using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One deterministic neighbor lookup over any ownership array: the polity ids owning a tile adjacent to a polity's
/// territory, ascending. One scan per call; operators read the same answer for the turn snapshot from
/// <see cref="TurnView.BorderingPolities"/>, which indexes every border in one pass.
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
