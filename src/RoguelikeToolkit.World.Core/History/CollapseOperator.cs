using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 72: an overextended, crowded polity breaks into successor polities and ends. Every turn, in polity
/// order, a polity that owns at least <see cref="MinTiles"/> tiles, holds at least <see cref="MinSettlements"/>
/// standing settlements and carries crowding pressure of <see cref="PressureThreshold"/> or more may collapse
/// (<see cref="CollapseChance"/> per turn). Its successors are its outlying settlements, picked far from the old
/// capital and from each other (at most <see cref="MaxPerCollapse"/>, and <see cref="HistoryContext.MaxSuccessors"/> over the whole history); the driver founds them with the old folk and
/// culture, razes the old seat into the displaced pool and re-floods the territory. Pins on the dead polity are
/// kept but inert. One collapse costs one emergence from this operator's own per-turn budget.
/// </summary>
public sealed class CollapseOperator : IHistoryOperator
{
    /// <summary>Minimum owned tiles before a polity can fall apart.</summary>
    public const int MinTiles = 20;

    /// <summary>Minimum standing settlements (the capital counts).</summary>
    public const int MinSettlements = 4;

    /// <summary>Crowding pressure (plus war bumps) at which the centre no longer holds.</summary>
    public const double PressureThreshold = 0.7;

    /// <summary>Per-polity, per-turn chance once every condition holds.</summary>
    public const double CollapseChance = 0.05;

    /// <summary>Most successor polities one collapse founds.</summary>
    public const int MaxPerCollapse = 3;

    /// <summary>Diagnostic name, <c>Collapse</c>.</summary>
    public string Name => "Collapse";

    /// <summary><see cref="StageOrder.Collapse"/> (72).</summary>
    public int Order => StageOrder.Collapse;

    /// <summary>Proposes this turn's collapses from the snapshot.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        var polities = view.Polities;
        if (polities.Count == 0) return;
        var vectors = view.Map.DataStore.GetTileVectors();
        int[] ownership = view.OwnershipSnapshot();
        var owned = new int[polities.Count];
        foreach (int owner in ownership)
            if ((uint)owner < (uint)owned.Length) owned[owner]++;
        var standing = new List<int>[polities.Count];
        foreach (var s in view.Settlements)
        {
            if (s.Ended || (uint)s.PolityId >= (uint)standing.Length) continue;
            (standing[s.PolityId] ??= new List<int>()).Add(s.Tile);
        }
        int room = view.MaxSuccessors;
        foreach (var p in polities)
            if (p.Parent >= 0 && !polities[p.Parent].Alive) room--;

        foreach (var polity in polities)
        {
            if (room < 2) return;
            if (!polity.Alive || polity.FoundedTurn >= view.Turn) continue;
            var tiles = standing[polity.Id];
            if (owned[polity.Id] < MinTiles || tiles == null || tiles.Count < MinSettlements) continue;
            if (view.ScalarOf(polity.Id, ScalarRegistry.Pressure) < PressureThreshold) continue;
            if (sink.Rng(polity.Id, "collapse").NextDouble() >= CollapseChance) continue;
            var successors = Pick(vectors, polity.CapitalTile, tiles, Math.Min(MaxPerCollapse, room));
            if (successors.Count < 2) continue;
            if (!sink.TrySpendEmergence()) continue;
            room -= successors.Count;
            sink.Emit(new Proposal.SplitPolity { EntityId = polity.Id, SuccessorTiles = successors });
        }
    }

    /// <summary>Farthest-point pick over the outlying settlement tiles: first the one farthest from the capital, then each next one farthest from the capital and the earlier picks.</summary>
    private static List<int> Pick(ReadOnlySpan<Vector3D> vectors, int capital, List<int> tiles, int count)
    {
        var picked = new List<int>(count);
        var seats = new List<int>(tiles);
        seats.Remove(capital);
        while (picked.Count < count && seats.Count > 0)
        {
            int best = -1;
            double bestNearest = double.MaxValue;
            foreach (int tile in seats)
            {
                double nearest = Vector3D.Dot(vectors[capital], vectors[tile]);
                foreach (int other in picked) nearest = Math.Max(nearest, Vector3D.Dot(vectors[other], vectors[tile]));
                if (nearest < bestNearest || (nearest == bestNearest && tile < best)) { bestNearest = nearest; best = tile; }
            }
            picked.Add(best);
            seats.Remove(best);
        }
        return picked;
    }
}
