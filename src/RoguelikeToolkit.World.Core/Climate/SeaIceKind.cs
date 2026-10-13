namespace RoguelikeToolkit.World.Core.Climate;

/// <summary>How much of the year an ocean tile is under ice.</summary>
public enum SeaIceKind : byte
{
    /// <summary>Open water all year (or a land tile: sea ice is an ocean condition).</summary>
    None = 0,
    /// <summary>Frozen over in winter, open in summer: the marginal ice zone and the fast ice of a cold coast.</summary>
    Seasonal = 1,
    /// <summary>Survives the summer: the polar pack.</summary>
    Perennial = 2
}
