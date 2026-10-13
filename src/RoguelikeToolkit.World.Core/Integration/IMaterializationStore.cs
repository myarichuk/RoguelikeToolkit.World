using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// Store for materialized-map state: per-address tile overrides plus bindings
/// from map addresses to game-side materialized location ids. Consumed via
/// <see cref="QueryOptions.Materialized"/>; null means pure geography.
/// </summary>
public interface IMaterializationStore
{
    /// <summary>Looks up the tile override stored for an address.</summary>
    /// <returns>True when an override exists; <paramref name="tileOverride"/> is then non-null.</returns>
    bool TryGetOverride(MapAddress address, out TileOverride? tileOverride);
    /// <summary>Returns the game-side location id bound to an address, or null when none is bound.</summary>
    string? GetBoundLocationId(MapAddress address);
    /// <summary>Binds an address to a game-side location id, replacing any previous binding.</summary>
    void BindLocation(MapAddress address, string locationId);
    /// <summary>Stores the override for an address, replacing any previous one.</summary>
    void SetOverride(MapAddress address, TileOverride tileOverride);
}
