using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Elevation;

public partial class ErosionGenerationStage
{
    private void GlacialPass(
        WorldDataStore store, Span<ElevationInfo> elev, Span<int> neighbors, bool hasClimate, Span<ClimateInfo> climate)
    {
        int n = store.TileCount;
        var vectors = store.GetTileVectors();
        var isGlacier = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (elev[i].Height < ElevationGenerationStage.SeaLevel) continue;
            isGlacier[i] = hasClimate
                ? Glaciology.IsGlacierTile(vectors[i], elev[i].Height, climate[i].Temperature, climate[i].Precipitation)
                : Glaciology.IsGlacierTile(vectors[i], elev[i].Height);
        }

        // Ice flux: each glacier tile accumulates its upstream ice area along
        // steepest descent (outlet glaciers carve hardest).
        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = elev[i].Height;
        var flux = new float[n];
        for (int i = 0; i < n; i++) flux[i] = isGlacier[i] ? 1f : 0f;
        var order = DescendingOrder(heights);
        float maxFlux = 1f;
        foreach (int i in order)
        {
            if (!isGlacier[i]) continue;
            int lowest = Hydrography.LowestNeighbor(store, heights, i, neighbors);
            if (lowest >= 0 && isGlacier[lowest]) flux[lowest] += flux[i];
        }
        for (int i = 0; i < n; i++) if (flux[i] > maxFlux) maxFlux = flux[i];

        // Order-independent mode records every move against the frozen surface and applies them together
        // (see ApplyMoves); the legacy mode writes through immediately, in tile order.
        bool jacobi = OrderIndependent;
        var moves = jacobi ? new List<(int From, int To, float Amount)>() : null;
        for (int g = 0; g < GlacialIterations; g++)
        {
            moves?.Clear();
            for (int i = 0; i < n; i++)
            {
                if (!isGlacier[i]) continue;
                int adjacent = store.GetAdjacent(i, neighbors);

                // Cirque check: a glacier head with no upstream ice plucks its
                // headwall harder (bergschrund quarrying).
                bool hasUpstreamIce = false;
                int highestWall = -1;
                float wallTop = elev[i].Height;
                for (int k = 0; k < adjacent; k++)
                {
                    int j = neighbors[k];
                    if (isGlacier[j] && elev[j].Height > elev[i].Height) hasUpstreamIce = true;
                    if (elev[j].Height > wallTop) { wallTop = elev[j].Height; highestWall = j; }
                }

                // Floor abrasion: flux-weighted, allowed slight slope inversion
                // so tongues gouge overdeepenings (future tarns).
                int lowest = -1;
                float best = elev[i].Height;
                for (int k = 0; k < adjacent; k++)
                {
                    int j = neighbors[k];
                    if (elev[j].Height < best) { best = elev[j].Height; lowest = j; }
                }
                if (lowest >= 0)
                {
                    double slope = elev[i].Height - best;
                    double q = flux[i] / maxFlux;
                    double carve = GlacialRate * (0.25 + 0.75 * q) * (0.3 + Math.Min(slope * 8.0, 1.2));
                    if (!hasUpstreamIce) carve *= 1.5;
                    float move = (float)Math.Min(carve, slope * 1.1);
                    if (move > 0) Move(elev, moves, i, lowest, move); // frontal moraine / outwash
                }

                // Wall plucking: U-widening draws the valley walls down into
                // the ice, which carries the debris away downstream.
                if (highestWall >= 0)
                {
                    double wallSlope = wallTop - elev[i].Height;
                    float wallMove = (float)Math.Min(wallSlope * GlacialRate * 0.3, wallSlope * 0.25);
                    if (wallMove > 0) Move(elev, moves, highestWall, i, wallMove);
                }
            }
            if (jacobi) ApplyMoves(store, elev, neighbors, moves!);
        }
    }

    private static void Move(Span<ElevationInfo> elev, List<(int From, int To, float Amount)>? moves, int from, int to, float amount)
    {
        if (moves != null) { moves.Add((from, to, amount)); return; }
        elev[from].Height -= amount;
        elev[to].Height += amount;
    }

    /// <summary>
    /// Applies one Jacobi sweep of glacial moves. Several glaciers can pluck the same wall tile in one
    /// sweep, which the in-place order used to limit by letting later plucks see the lowered wall. The same
    /// bound is kept explicitly: a source gives up at most 1.1× its drop to its lowest neighbor (the single-move
    /// limit of the floor abrasion), the moves it makes scaled down together when they would exceed it.
    /// </summary>
    private static void ApplyMoves(
        WorldDataStore store, Span<ElevationInfo> elev, Span<int> neighbors, List<(int From, int To, float Amount)> moves)
    {
        if (moves.Count == 0) return;
        var outgoing = new Dictionary<int, float>();
        foreach (var (from, _, amount) in moves)
            outgoing[from] = outgoing.TryGetValue(from, out float o) ? o + amount : amount;

        var scale = new Dictionary<int, float>(outgoing.Count);
        foreach (var (tile, total) in outgoing)
        {
            int adjacent = store.GetAdjacent(tile, neighbors);
            float lowest = elev[tile].Height;
            for (int k = 0; k < adjacent; k++)
                if (elev[neighbors[k]].Height < lowest) lowest = elev[neighbors[k]].Height;
            float cap = (elev[tile].Height - lowest) * 1.1f;
            scale[tile] = total > cap ? Math.Max(0f, cap) / total : 1f;
        }

        // Accumulate first, add after: no move sees another's effect. Per-tile sums still add in list order
        // (tile order), which can differ in the last ulp but never in which move wins.
        var delta = new Dictionary<int, float>();
        foreach (var (from, to, amount) in moves)
        {
            float a = amount * scale[from];
            delta[from] = delta.TryGetValue(from, out float f) ? f - a : -a;
            delta[to] = delta.TryGetValue(to, out float t) ? t + a : a;
        }
        foreach (var (tile, d) in delta) elev[tile].Height += d;
    }

    private static int[] DescendingOrder(float[] surface)
    {
        return TileOrdering.DescendingByValue(surface);
    }
}
