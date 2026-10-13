namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Sparse feature: one connected water region (tiles + boundary loop).</summary>
public sealed class WaterBody
{
    public int Id { get; }
    public WaterBodyKind Kind { get; }
    public List<int> Tiles { get; } = new();
    public List<int> Boundary { get; } = new();

    public WaterBody(int id, WaterBodyKind kind)
    {
        Id = id;
        Kind = kind;
    }
}
