using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Managed catalogs for sparse features. Field layers stay in
/// <see cref="WorldDataStore"/>; these catalogs hold tile-id lists plus
/// WKT export. Not part of the memory-mapped store.
/// </summary>
public sealed class RiverCatalog
{
    public List<River> Rivers { get; } = new();
}
