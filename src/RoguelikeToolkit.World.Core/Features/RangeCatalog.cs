namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Managed catalog of mountain ranges and valleys.</summary>
public sealed class RangeCatalog
{
    /// <summary>All range features. As built by <see cref="Hydrology.RangeCatalogBuilder"/> the order is mountain ranges, canyons, playas, then valleys, and <see cref="RangeFeature.Id"/> equals the list index.</summary>
    public List<RangeFeature> Features { get; } = new();
}
