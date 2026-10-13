namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Sparse feature: one connected water region (tiles + boundary loop).</summary>
public sealed class WaterBody
{
    /// <summary>The <see cref="Hydrology.HydrologyInfo.WaterBodyId"/> shared by every tile of this body. Not the list index in <see cref="WaterBodyCatalog.Bodies"/>.</summary>
    public int Id { get; }
    /// <summary>Lake, sea or ocean; see <see cref="WaterBodyKind"/> for how the generator classifies them.</summary>
    public WaterBodyKind Kind { get; }
    /// <summary>Indices of every tile in the body, in ascending order.</summary>
    public List<int> Tiles { get; } = new();
    /// <summary>The subset of <see cref="Tiles"/> that has at least one neighbour outside the body, in ascending tile-index order (a set of shore tiles, not an ordered loop).</summary>
    public List<int> Boundary { get; } = new();

    /// <summary>Creates a body with no tiles; the generator fills <see cref="Tiles"/> and <see cref="Boundary"/>.</summary>
    public WaterBody(int id, WaterBodyKind kind)
    {
        Id = id;
        Kind = kind;
    }
}
