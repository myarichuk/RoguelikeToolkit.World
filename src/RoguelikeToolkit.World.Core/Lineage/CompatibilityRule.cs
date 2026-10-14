namespace RoguelikeToolkit.World.Core.Lineage;

/// <summary>What a folk pair produces: blended children, dominant-folk children, or no children.</summary>
public enum CompatibilityRule
{
    /// <summary>The child blends both lineages (default for unlisted pairs).</summary>
    Blend = 0,
    /// <summary>The child takes the dominant folk's lineage.</summary>
    Dominant = 1,
    /// <summary>The pair bears no children; union attempts reject.</summary>
    Sterile = 2,
}
