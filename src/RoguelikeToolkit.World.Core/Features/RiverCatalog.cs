using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Features;

/// <summary>
/// Managed catalogs for sparse features. Field layers stay in
/// <see cref="WorldDataStore"/>; these catalogs hold tile-id lists plus
/// WKT export. Not part of the memory-mapped store.
/// </summary>
public sealed class RiverCatalog
{
    /// <summary>All cataloged rivers, in <see cref="River.Id"/> order. Limited to multi-tile reaches: a lone river tile is flagged in the hydrology layer but has no entry here.</summary>
    public List<River> Rivers { get; } = new();
}
