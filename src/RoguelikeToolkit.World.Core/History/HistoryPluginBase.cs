using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Base for history stages: resolves the <see cref="HistoryCatalog"/> and
/// <see cref="HistoryContext"/> the builder attached to the map, applies
/// <see cref="IHistoryPlugin.Precondition"/>, then
/// <see cref="IHistoryPlugin.Apply"/>. A stage with no attachments (a physics-only
/// pipeline) is a silent no-op, so history stages never break minimal builds.
/// </summary>
public abstract class HistoryPluginBase : IHistoryPlugin, IDeclaredStage, IStageNamed
{
    /// <summary>Diagnostic name.</summary>
    public abstract string Name { get; }
    /// <summary>Execution order (see <see cref="StageOrder"/>).</summary>
    public abstract int Order { get; }
    /// <summary>Field-layer struct types this stage requires.</summary>
    public abstract Type[] Reads { get; }
    /// <summary>Field-layer struct types this stage reads only if registered.</summary>
    public virtual Type[] ReadsOptional => Array.Empty<Type>();
    /// <summary>History stages fill the attached catalog, never a tile layer.</summary>
    public virtual Type[] Writes => Array.Empty<Type>();

    /// <summary>Whether the plugin has work to do.</summary>
    public abstract bool Precondition(WorldMap map, HistoryCatalog history);

    /// <summary>Runs the plugin's turns.</summary>
    public abstract void Apply(WorldMap map, HistoryCatalog history, HistoryContext context);

    /// <summary>Resolves attachments, gates on <see cref="Precondition"/>, runs <see cref="Apply"/>.</summary>
    public void Execute(WorldMap map)
    {
        var history = map.GetAttachment<HistoryCatalog>();
        var context = map.GetAttachment<HistoryContext>();
        if (history is null || context is null) return;
        if (!Precondition(map, history)) return;
        Apply(map, history, context);
    }
}
