using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RoguelikeToolkit.World.Core;

public sealed class RangeFeature
{
    public int Id { get; }
    public RangeKind Kind { get; }
    public List<int> Tiles { get; } = new();

    public RangeFeature(int id, RangeKind kind)
    {
        Id = id;
        Kind = kind;
    }
}
