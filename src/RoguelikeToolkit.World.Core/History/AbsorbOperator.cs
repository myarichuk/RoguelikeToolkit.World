using RoguelikeToolkit.World.Core.Generation;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 73: a small polity folds into a much larger bordering neighbour of its own folk. Every turn, in polity order,
/// a polity that has stood at least <see cref="MinAge"/> turns and owns at most <see cref="MaxTiles"/> tiles may be absorbed
/// (<see cref="AbsorbChance"/> per turn) by the largest bordering polity of the same folk that owns at least
/// <see cref="SizeRatio"/> times as many tiles (lowest id on a tie). A polity that is absorbing or being absorbed this
/// turn takes part in no other absorption. The driver hands over land, settlements and holy sites.
/// </summary>
public sealed class AbsorbOperator : IHistoryOperator
{
    /// <summary>Most tiles a polity may own and still be absorbed.</summary>
    public const int MaxTiles = 8;

    /// <summary>The absorber must own at least this many times the absorbed polity's tiles.</summary>
    public const int SizeRatio = 3;

    /// <summary>Turns a polity must have stood before a neighbour can take it.</summary>
    public const int MinAge = 10;

    /// <summary>Per-polity, per-turn chance once every condition holds.</summary>
    public const double AbsorbChance = 0.04;

    /// <summary>Diagnostic name, <c>Absorb</c>.</summary>
    public string Name => "Absorb";

    /// <summary><see cref="StageOrder.Absorb"/> (73).</summary>
    public int Order => StageOrder.Absorb;

    /// <summary>Proposes this turn's absorptions from the snapshot.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        var polities = view.Polities;
        if (polities.Count < 2) return;
        int[] ownership = view.OwnershipSnapshot();
        var owned = new int[polities.Count];
        foreach (int owner in ownership)
            if ((uint)owner < (uint)owned.Length) owned[owner]++;
        var busy = new HashSet<int>();
        foreach (var polity in polities)
        {
            if (!polity.Alive || view.Turn - polity.FoundedTurn < MinAge || busy.Contains(polity.Id)) continue;
            if (owned[polity.Id] > MaxTiles) continue;
            if (sink.Rng(polity.Id, "absorb").NextDouble() >= AbsorbChance) continue;
            int best = -1;
            foreach (int other in HistoryNeighbors.Bordering(view.Map.DataStore, ownership, polity.Id))
            {
                var o = polities[other];
                if (!o.Alive || busy.Contains(other) || o.FolkKey != polity.FolkKey) continue;
                if (owned[other] < owned[polity.Id] * SizeRatio) continue;
                if (best < 0 || owned[other] > owned[best]) best = other;
            }
            if (best < 0) continue;
            busy.Add(polity.Id);
            busy.Add(best);
            sink.Emit(new Proposal.AbsorbPolity { EntityId = polity.Id, Into = best });
        }
    }
}
