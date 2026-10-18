namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One entry in the append-only <see cref="HistoryCatalog.Events"/> log. Tag-only by design:
/// no rendered prose is stored here (myth and name text render late from tags in P2c/P3), so the
/// log is byte-identical with every text provider stubbed out.
/// </summary>
public sealed class HistoryEvent
{
    /// <summary>
    /// Stable id within the catalog (assigned on append, never reused, survives compaction), or -1 for an
    /// event not yet in a log. <see cref="Cause"/> refers to it, so compaction cannot shift references.
    /// In-memory only until the schema v4 save (M1.4); loads renumber in log order.
    /// </summary>
    public int Id { get; internal set; } = -1;
    /// <summary>What happened.</summary>
    public HistoryEventKind Kind { get; init; }
    /// <summary>For <see cref="HistoryEventKind.Plugin"/> events, the plugin's kind name (<c>namespace/name</c>); empty for built-ins.</summary>
    public string PluginKind { get; init; } = string.Empty;
    /// <summary>History turn the event lands on.</summary>
    public int Turn { get; init; }
    /// <summary>Acting polity id, or -1 when the event belongs to no polity (e.g. a fork marker).</summary>
    public int PolityId { get; init; } = -1;
    /// <summary>Planet tile the event sits on, or -1 when it is not attached to a tile.</summary>
    public int TileIndex { get; init; } = -1;
    /// <summary>Id of the event that caused this one, or -1. A cited event is protected from compaction.</summary>
    public int Cause { get; init; } = -1;
    /// <summary>Compaction class; defaults from <see cref="EventImportanceMap"/>.</summary>
    public EventImportance Importance { get; init; }
    /// <summary>Entities involved and their roles; empty for events that only carry tags.</summary>
    public List<EventParticipant> Participants { get; } = new();
    /// <summary>Machine-readable detail (form, folk and culture keys for a founding); the rendering contract, never prose.</summary>
    public List<string> Tags { get; } = new();
}
