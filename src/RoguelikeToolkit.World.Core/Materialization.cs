using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Per-tile override applied on top of generated geography. Replacement (not
/// additive): a set field wins over the generated value at query time.
/// </summary>
public sealed class TileOverride
{
    public BiomeType? Biome { get; init; }
    public int? DangerLevel { get; init; }
}

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

/// <summary>In-memory <see cref="IMaterializationStore"/>.</summary>
public sealed class MaterializationStore : IMaterializationStore
{
    private readonly Dictionary<MapAddress, TileOverride> _overrides = new();
    private readonly Dictionary<MapAddress, string> _bindings = new();

    public bool TryGetOverride(MapAddress address, out TileOverride? tileOverride)
    {
        if (_overrides.TryGetValue(address, out var found))
        {
            tileOverride = found;
            return true;
        }
        tileOverride = null;
        return false;
    }

    public string? GetBoundLocationId(MapAddress address)
        => _bindings.TryGetValue(address, out var id) ? id : null;

    public void BindLocation(MapAddress address, string locationId)
    {
        if (string.IsNullOrEmpty(locationId)) throw new ArgumentException("Location id must be non-empty.", nameof(locationId));
        _bindings[address] = locationId;
    }

    public void SetOverride(MapAddress address, TileOverride tileOverride)
    {
        ArgumentNullException.ThrowIfNull(tileOverride);
        _overrides[address] = tileOverride;
    }
}
