namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One projected historic site: in P2a every polity capital, surfaced through
/// <see cref="HistoryCatalog"/> as <c>Settlement</c>-kind game sites. Ruins, battlefields
/// and holy sites arrive with the P2b/P2c operators, which append entries here; P2a only
/// ever writes (and deactivates) capital sites, with zero score deltas.
/// </summary>
public sealed class HistorySite
{
    /// <summary>Planet tile the site sits on.</summary>
    public int TileIndex { get; init; }
    /// <summary>Owning polity id.</summary>
    public int PolityId { get; init; }
    /// <summary>History turn the site was founded on.</summary>
    public int FoundedTurn { get; init; }
    /// <summary>Geographic position of the tile, stored at projection time so queries need no tile store.</summary>
    public Primitives.GeoCoord Position { get; init; }
    /// <summary>False when the tile no longer supports the site (drowned by an edit); the entry is kept for provenance.</summary>
    public bool Active { get; set; } = true;
}
