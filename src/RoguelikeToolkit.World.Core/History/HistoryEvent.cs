namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One entry in the append-only <see cref="HistoryCatalog.Events"/> log. Tag-only by design:
/// no rendered prose is stored here (myth and name text render late from tags in P2c/P3), so the
/// log is byte-identical with every text provider stubbed out.
/// </summary>
public sealed class HistoryEvent
{
    /// <summary>What happened.</summary>
    public HistoryEventKind Kind { get; init; }
    /// <summary>History turn the event lands on.</summary>
    public int Turn { get; init; }
    /// <summary>Acting polity id, or -1 when the event belongs to no polity (e.g. a fork marker).</summary>
    public int PolityId { get; init; } = -1;
    /// <summary>Planet tile the event sits on, or -1 when it is not attached to a tile.</summary>
    public int TileIndex { get; init; } = -1;
    /// <summary>Machine-readable detail (form, folk and culture keys for a founding); the rendering contract, never prose.</summary>
    public List<string> Tags { get; } = new();
}
