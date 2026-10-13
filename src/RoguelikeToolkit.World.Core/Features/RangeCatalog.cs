namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Managed catalog of mountain ranges and valleys.</summary>
public sealed class RangeCatalog
{
    public List<RangeFeature> Features { get; } = new();
}
