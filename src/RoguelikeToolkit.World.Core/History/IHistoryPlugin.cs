using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One history simulation step as a pipeline stage (orders 26..91): a precondition gate plus the
/// turn-loop body. Built-in history stages derive from <see cref="HistoryPluginBase"/>; game-side
/// conflict operators (war, conquest, rebellion, trade in P2b) implement this interface directly or
/// derive from the base, and run among the built-ins in deterministic pipeline order.
/// </summary>
public interface IHistoryPlugin : IWorldGeneratorStage
{
    /// <summary>Whether the plugin has work to do: the catalog and map are ready and its output is not already built.</summary>
    /// <param name="map">Map whose store holds the physics layers.</param>
    /// <param name="history">Catalog under construction (attached to <paramref name="map"/> by the builder).</param>
    bool Precondition(WorldMap map, HistoryCatalog history);

    /// <summary>Runs the plugin's turns against <paramref name="map"/>, appending to <paramref name="history"/>.</summary>
    /// <param name="map">Map whose store holds the physics layers.</param>
    /// <param name="history">Catalog under construction.</param>
    /// <param name="context">Turn length, span and per-turn budgets.</param>
    void Apply(WorldMap map, HistoryCatalog history, HistoryContext context);
}
