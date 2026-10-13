namespace RoguelikeToolkit.World.Core.Lithosphere;

/// <summary>Sparse feature: one ore/fuel/mineral occurrence on a tile.</summary>
public sealed class Deposit
{
    /// <summary>Catalog id; ascending in build order (by <see cref="DepositType"/>, then tile index).</summary>
    public int Id { get; }
    /// <summary>The commodity.</summary>
    public DepositType Type { get; }
    /// <summary>Index of the tile that hosts the deposit; the builder places at most one deposit of a type per tile.</summary>
    public int TileIndex { get; }
    /// <summary>Prospectivity score as built, in [0.35, 1] (the catalog builder drops lower scores and caps at 1); <see cref="Size"/> is derived from it. The constructor itself does not clamp.</summary>
    public float Richness { get; }
    /// <summary>Class derived from <see cref="Richness"/>: 0.85 and above is Rich, 0.65 Large, 0.45 Medium, otherwise Small.</summary>
    public DepositSize Size { get; }

    /// <summary>Creates a deposit and derives <see cref="Size"/> from <paramref name="richness"/>.</summary>
    public Deposit(int id, DepositType type, int tileIndex, float richness)
    {
        Id = id;
        Type = type;
        TileIndex = tileIndex;
        Richness = richness;
        Size = richness >= 0.85f ? DepositSize.Rich
            : richness >= 0.65f ? DepositSize.Large
            : richness >= 0.45f ? DepositSize.Medium
            : DepositSize.Small;
    }
}
