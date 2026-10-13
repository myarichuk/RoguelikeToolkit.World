namespace RoguelikeToolkit.World.Core.Storage;

/// <summary>What a tile's <see cref="UserEditInfo"/> overrides.</summary>
[Flags]
public enum UserEditFlags : byte
{
    /// <summary>The tile has no pins; generation decides everything (sculpt and climate deltas may still apply).</summary>
    None = 0,
    /// <summary>The surface height is fixed to <see cref="UserEditInfo.PinHeight"/> after erosion ("fix", not "sculpt").</summary>
    PinHeight = 1,
    /// <summary>The biome is fixed to <see cref="UserEditInfo.PinBiome"/>: hydrology classifies its cover from it and the biome stage keeps it.</summary>
    PinBiome = 2,
    /// <summary>The land use is fixed to <see cref="UserEditInfo.LandUse"/> / <see cref="UserEditInfo.LandActivities"/>, whatever the allocation stages decided. Land use is a decision, never a physics input.</summary>
    PinLandUse = 4,
}
