namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 35: walks every standing settlement along the tier ladder with its fill (people over catchment capacity). A settlement
/// rises one tier when its fill reaches that tier's <see cref="UpAt"/> threshold and falls one when it drops below the
/// <see cref="DownAt"/> threshold, which sits well under the up one: between the two it holds, so a settlement hovering near a
/// boundary does not flip every turn (hysteresis). Settlements younger than <see cref="GraceTurns"/> are left alone while
/// their first people arrive, and a capital never falls below <see cref="CapitalFloor"/>. No RNG, no budgets; at most one step per
/// settlement per turn.
/// </summary>
public sealed class TierOperator : IHistoryOperator
{
    /// <summary>Lowest tier a capital settlement can fall to.</summary>
    public const SettlementTier CapitalFloor = SettlementTier.Town;
    /// <summary>Turns after founding during which a settlement keeps its starting tier.</summary>
    public const int GraceTurns = 10;
    /// <summary>Fill at which a settlement rises into the tier at the same index (hamlet is the bottom and has none).</summary>
    public static readonly double[] UpAt = { 0, 0.25, 0.5, 0.8 };
    /// <summary>Fill below which a settlement falls out of the tier at the same index.</summary>
    public static readonly double[] DownAt = { 0, 0.12, 0.3, 0.5 };

    /// <summary>Diagnostic name, <c>Tier</c>.</summary>
    public string Name => "Tier";

    /// <summary><see cref="Generation.StageOrder.Tier"/> (35).</summary>
    public int Order => Generation.StageOrder.Tier;

    /// <summary>Proposes one-step tier changes for settlements whose fill crossed a threshold.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        foreach (var s in view.Settlements)
        {
            if (s.Ended || view.Turn - s.FoundedTurn < GraceTurns) continue;
            double capacity = view.SettlementCapacity(s.Index);
            if (!(capacity > 0)) continue;
            double fill = s.Population / capacity;
            int tier = (int)s.Tier;
            if (tier < (int)SettlementTier.City && fill >= UpAt[tier + 1])
                sink.Emit(new Proposal.ChangeTier { EntityId = s.PolityId, Settlement = s.Index, Tier = (SettlementTier)(tier + 1) });
            else if (tier > (int)SettlementTier.Hamlet && fill < DownAt[tier]
                && !(s.Role == SettlementRole.Capital && (SettlementTier)(tier - 1) < CapitalFloor))
                sink.Emit(new Proposal.ChangeTier { EntityId = s.PolityId, Settlement = s.Index, Tier = (SettlementTier)(tier - 1) });
        }
    }
}
