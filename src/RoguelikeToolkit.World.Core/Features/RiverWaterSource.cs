namespace RoguelikeToolkit.World.Core.Features;

/// <summary>Where a river tile's water comes from (head classification).</summary>
public enum RiverWaterSource : byte
{
    /// <summary>No source classified: the tile is not a river tile, or the elevation layer needed to trace upstream is missing.</summary>
    None = 0,
    /// <summary>Rain/groundwater-fed head: no glacier upstream.</summary>
    Spring = 1,
    /// <summary>The river's head sits on glaciated ground (meltwater-fed).</summary>
    GlacierMelt = 2
}
