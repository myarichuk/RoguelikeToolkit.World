namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One settled place owned by a polity: in P2a every polity's capital, later (P2b/P4) towns
/// founded along roads and conquest. The settlement adapter (P4a) reads this list, never the
/// tile layers, to place capitals and road access.
/// </summary>
public sealed class HistorySettlement
{
    /// <summary>Planet tile the settlement stands on.</summary>
    public int TileIndex { get; init; }
    /// <summary>Owning polity id.</summary>
    public int PolityId { get; set; }
    /// <summary>History turn the settlement was founded on.</summary>
    public int FoundedTurn { get; init; }
    /// <summary>Geographic position of the tile, stored at projection time so queries need no tile store.</summary>
    public Primitives.GeoCoord Position { get; init; }
    /// <summary>Capacity-derived habitability 0..1 (0.5 when no capacity layer was registered); re-resolved on every map edit.</summary>
    public float Habitability { get; set; } = 0.5f;
    /// <summary>False when the tile no longer supports the settlement (drowned or landlocked by an edit); causes and dates are never rewritten.</summary>
    public bool Active { get; set; } = true;
    /// <summary>History turn the settlement ended (razed, abandoned, drowned), or -1 while it stands.</summary>
    public int EndedTurn { get; set; } = -1;
    /// <summary>Short cause key of the end (<c>razed</c>, <c>abandoned</c>, <c>quake</c>, ...), empty while it stands.</summary>
    public string EndCause { get; set; } = string.Empty;
    /// <summary>Size class.</summary>
    public SettlementTier Tier { get; set; } = SettlementTier.City;
    /// <summary>Why the settlement exists.</summary>
    public SettlementRole Role { get; set; } = SettlementRole.Capital;
    /// <summary>Index into <see cref="HistoryCatalog.Settlements"/> of the settlement this one was founded from, or -1.</summary>
    public int Parent { get; set; } = -1;
    /// <summary>True once <see cref="EndedTurn"/> is set; a historical fact, unlike the physical <see cref="Active"/> flag.</summary>
    public bool Ended => EndedTurn >= 0;
}
