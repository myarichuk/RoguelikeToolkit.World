using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 28: the turn driver at the head of the history-operator band (28..90). Advances the
/// <see cref="HistoryContext"/> clock from turn 1 through the end of the configured span so the
/// per-turn budget ledger is exercised every turn, clears the per-operator ledgers for the band,
/// and records the simulated span on the catalog. The driver appends no events itself; the
/// conflict operators plug into the band behind it (orders 30..80) and spend their own budgets
/// through the context while looping the same turn range.
/// </summary>
public sealed class HistoryEventsStage : HistoryPluginBase
{
    /// <summary>Diagnostic name, <c>HistoryEvents</c>.</summary>
    public override string Name => "HistoryEvents";
    /// <summary><see cref="StageOrder.HistoryEvents"/> (28).</summary>
    public override int Order => StageOrder.HistoryEvents;
    /// <summary>None: the driver only advances the clock.</summary>
    public override Type[] Reads => Array.Empty<Type>();

    /// <summary>True once polities have formed (even when zero formed on an all-ocean world).</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history) => history.PolitiesFormed;

    /// <summary>Runs the clock; the simulated head lands on the last configured turn.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ClearOperatorBudgets();
        for (int turn = 1; turn < context.TurnCount; turn++) context.BeginTurn(turn);
        history.SimulatedTurns = context.TurnCount > 0 ? context.TurnCount - 1 : 0;
    }
}
