using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Integration;

/// <summary>
/// Store for materialized-map state: per-address tile overrides plus bindings
/// from map addresses to game-side materialized location ids. Consumed via
/// <see cref="QueryOptions.Materialized"/>; null means pure geography.
/// </summary>
public interface IMaterializationStore
{
    bool TryGetOverride(MapAddress address, out TileOverride? tileOverride);
    string? GetBoundLocationId(MapAddress address);
    void BindLocation(MapAddress address, string locationId);
    void SetOverride(MapAddress address, TileOverride tileOverride);
}
