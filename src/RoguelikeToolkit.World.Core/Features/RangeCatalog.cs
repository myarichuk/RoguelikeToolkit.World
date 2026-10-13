using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoguelikeToolkit.World.Core;

/// <summary>Managed catalog of mountain ranges and valleys.</summary>
public sealed class RangeCatalog
{
    public List<RangeFeature> Features { get; } = new();
}
