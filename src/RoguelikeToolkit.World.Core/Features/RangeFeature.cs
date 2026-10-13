namespace RoguelikeToolkit.World.Core.Features;

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
