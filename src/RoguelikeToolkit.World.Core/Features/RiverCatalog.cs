using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Features;

/// <summary>
/// Managed catalogs for sparse features. Field layers stay in
/// <see cref="WorldDataStore"/>; these catalogs hold tile-id lists plus
/// WKT export. Not part of the memory-mapped store.
/// </summary>
public sealed class RiverCatalog
{
    public List<River> Rivers { get; } = new();
}
