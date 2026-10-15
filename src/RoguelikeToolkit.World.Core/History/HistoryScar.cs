using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// A lasting mark a shock leaves on a tile: a ruin where a settlement was abandoned, a crater, a plague pit, a scorched field.
/// Projects through <see cref="HistoryCatalog"/> as a <c>Ruin</c>-kind game site tagged with <see cref="Kind"/> and <see cref="Cause"/>.
/// </summary>
public sealed class HistoryScar
{
    /// <summary>Scar left where a settlement was abandoned.</summary>
    public const string Ruin = "ruin";
    /// <summary>Scar left by a volcanic eruption.</summary>
    public const string Crater = "crater";
    /// <summary>Scar left by a plague.</summary>
    public const string PlaguePit = "plague_pit";
    /// <summary>Scar left by drought or famine.</summary>
    public const string ScorchedField = "scorched_field";
    /// <summary>Scar left by an earthquake or a flood.</summary>
    public const string Fissure = "fissure";

    /// <summary>Planet tile the scar sits on.</summary>
    public int TileIndex { get; init; }
    /// <summary>Polity that owned the place when the scar was made, or -1.</summary>
    public int PolityId { get; init; }
    /// <summary>History turn the scar was made on.</summary>
    public int FoundedTurn { get; init; }
    /// <summary>What was left behind (<see cref="Ruin"/>, <see cref="Crater"/>...); plugins may use their own keys.</summary>
    public string Kind { get; init; } = Ruin;
    /// <summary>The shock kind or end cause that made it.</summary>
    public string Cause { get; init; } = string.Empty;
    /// <summary>Geographic position of the tile, stored at creation so queries need no tile store.</summary>
    public GeoCoord Position { get; init; }
    /// <summary>False when the tile no longer supports the scar (drowned by an edit); the entry is kept for provenance.</summary>
    public bool Active { get; set; } = true;
}
