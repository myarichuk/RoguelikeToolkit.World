using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One raised holy site: a high-surplus owned tile (never the capital) whose aura shelters its
/// polity (see <see cref="HolySiteStage"/>) and whose violation trips the desecration response.
/// Projects through <see cref="HistoryCatalog"/> as a shrine-kind game site.
/// </summary>
public sealed class HistoryHolySite
{
    /// <summary>Planet tile the site sits on.</summary>
    public int TileIndex { get; init; }
    /// <summary>Owning polity id.</summary>
    public int PolityId { get; init; }
    /// <summary>History turn the site was raised on.</summary>
    public int FoundedTurn { get; init; }
    /// <summary>Geographic position of the tile, stored at founding time so queries need no tile store.</summary>
    public GeoCoord Position { get; init; }
    /// <summary>False when the tile no longer supports the site (drowned by an edit); the entry is kept for provenance.</summary>
    public bool Active { get; set; } = true;
}
