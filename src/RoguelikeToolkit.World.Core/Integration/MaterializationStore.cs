using RoguelikeToolkit.World.Core.Hierarchy;

namespace RoguelikeToolkit.World.Core.Integration;

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
