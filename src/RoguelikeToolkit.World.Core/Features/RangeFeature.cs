namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Sparse feature: one edge-connected set of tiles sharing a <see cref="RangeKind"/> (a mountain range, valley, canyon reach or playa).</summary>
public sealed class RangeFeature
{
    /// <summary>Catalog id, unique across all kinds within one <see cref="RangeCatalog"/>.</summary>
    public int Id { get; }
    /// <summary>What the tiles are: high ground, river-side lowland, canyon reach or dry basin floor.</summary>
    public RangeKind Kind { get; }
    /// <summary>Member tile indices in breadth-first discovery order from the lowest-index seed tile; the tiles form one connected component and are not sorted.</summary>
    public List<int> Tiles { get; } = new();

    /// <summary>Creates an empty feature; the builder fills <see cref="Tiles"/>.</summary>
    public RangeFeature(int id, RangeKind kind)
    {
        Id = id;
        Kind = kind;
    }
}
