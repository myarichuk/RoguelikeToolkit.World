namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// One trade-road segment between two adjacent tiles on a route between two capitals, kept by a
/// polity. Filled by <c>TradeRoadOperator</c> from surplus-complementary pairs (river, coastal and
/// low-danger steps preferred) and severed in war; <c>ExchangeStage</c> diffuses along the
/// segments still active. The settlement adapter (P4a) reads road access from here. Segments of
/// one route share <see cref="RouteA"/>, <see cref="RouteB"/> and <see cref="FoundedTurn"/>.
/// </summary>
public sealed class HistoryRoad
{
    /// <summary>One endpoint tile.</summary>
    public int TileA { get; init; }
    /// <summary>The other endpoint tile.</summary>
    public int TileB { get; init; }
    /// <summary>Polity id keeping the road, or -1 for a road between polities.</summary>
    public int PolityId { get; init; } = -1;
    /// <summary>Route endpoint: the smaller of the two capital tiles this segment belongs to.</summary>
    public int RouteA { get; init; }
    /// <summary>Route endpoint: the larger of the two capital tiles this segment belongs to.</summary>
    public int RouteB { get; init; }
    /// <summary>History turn the route opened on.</summary>
    public int FoundedTurn { get; init; }
    /// <summary>True once a war severed the route; severed segments carry no exchange.</summary>
    public bool Severed { get; set; }
    /// <summary>History turn the route was severed on (meaningful only when <see cref="Severed"/>).</summary>
    public int SeveredTurn { get; set; }
}
