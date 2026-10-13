namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Detail knobs for one child-map tier. The warp-strength parameters
/// (<see cref="AcrossStrikeScale"/>, <see cref="AlongStrikeScale"/>) deform the
/// noise domain around the parent orogeny strike: coordinates across the strike
/// are compressed (higher ridge frequency) while coordinates along it are
/// elongated (smooth ridge crests). The region tier follows the parent strike
/// tightly; the local tier uses looser defaults.
/// </summary>
public sealed class RegionDetailOptions
{
    /// <summary>Lattice units spanned by the map half-extent before warping.</summary>
    public double DetailFrequency { get; set; } = 1.0;
    /// <summary>Across-strike coordinate multiplier (&gt;1 compresses: tighter ridges).</summary>
    public double AcrossStrikeScale { get; set; } = 3.0;
    /// <summary>Along-strike coordinate multiplier (&lt;1 elongates: smoother crests).</summary>
    public double AlongStrikeScale { get; set; } = 0.2;
    /// <summary>Peak detail height added (or removed) in the map interior.</summary>
    public float DetailAmplitude { get; set; } = 0.12f;
    /// <summary>Edge-pinning blend width in cells (outer ring is fully pinned).</summary>
    public double EdgePinCells { get; set; } = 2.0;
    /// <summary>Depth carved along a threaded river reach (tapered at both ends).</summary>
    public float RiverCarveDepth { get; set; } = 0.08f;
    /// <summary>Parent <see cref="ParentContext.Flow"/> at or above this threads a river.</summary>
    public float RiverFlowThreshold { get; set; } = 6f;
    public int NoiseOctaves { get; set; } = 3;
    /// <summary>Ridged-Fbm octave gain (&lt;0.5 emphasizes large strike-parallel structure).</summary>
    public double DetailGain { get; set; } = 0.5;

    /// <summary>Tight strike coupling for the region tier.</summary>
    public static RegionDetailOptions RegionDefault => new()
    {
        DetailFrequency = 1.0,
        AcrossStrikeScale = 3.0,
        AlongStrikeScale = 0.2,
        DetailAmplitude = 0.12f,
        EdgePinCells = 2.0,
        RiverCarveDepth = 0.08f,
        RiverFlowThreshold = 6f,
        NoiseOctaves = 3,
        DetailGain = 0.5,
    };

    /// <summary>Looser strike coupling for the local tier.</summary>
    public static RegionDetailOptions LocalDefault => new()
    {
        DetailFrequency = 1.0,
        AcrossStrikeScale = 1.8,
        AlongStrikeScale = 0.5,
        DetailAmplitude = 0.15f,
        EdgePinCells = 2.0,
        RiverCarveDepth = 0.09f,
        RiverFlowThreshold = 6f,
        NoiseOctaves = 3,
        DetailGain = 0.5,
    };
}
