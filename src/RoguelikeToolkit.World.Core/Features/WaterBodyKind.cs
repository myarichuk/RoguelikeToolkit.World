namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Water body kinds for game queries (lake vs sea vs ocean basin).</summary>
public enum WaterBodyKind : byte
{
    /// <summary>Enclosed water: any body above sea level, and any marine basin of at most one fortieth of the world's tiles that is not the ocean (a landlocked sea such as the Caspian).</summary>
    Lake = 0,
    /// <summary>A marginal sea: a marine body below sea level that is larger than one fortieth of the world's tiles but is not the largest.</summary>
    Sea = 1,
    /// <summary>The world ocean: the largest body below sea level. At most one per world.</summary>
    Ocean = 2
}
