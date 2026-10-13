using RoguelikeToolkit.World.Core.Generation;
namespace RoguelikeToolkit.World.Core.Hierarchy;

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
    public double DetailFrequency { get => _detailFrequency; set => _detailFrequency = StageKnob.Finite(value, nameof(DetailFrequency)); }
    private double _detailFrequency = 1.0;
    /// <summary>Across-strike coordinate multiplier (&gt;1 compresses: tighter ridges).</summary>
    public double AcrossStrikeScale { get => _acrossStrikeScale; set => _acrossStrikeScale = StageKnob.Finite(value, nameof(AcrossStrikeScale)); }
    private double _acrossStrikeScale = 3.0;
    /// <summary>Along-strike coordinate multiplier (&lt;1 elongates: smoother crests).</summary>
    public double AlongStrikeScale { get => _alongStrikeScale; set => _alongStrikeScale = StageKnob.Finite(value, nameof(AlongStrikeScale)); }
    private double _alongStrikeScale = 0.2;
    /// <summary>Peak detail height added (or removed) in the map interior.</summary>
    public float DetailAmplitude { get => _detailAmplitude; set => _detailAmplitude = StageKnob.Finite(value, nameof(DetailAmplitude)); }
    private float _detailAmplitude = 0.12f;
    /// <summary>Edge-pinning blend width in cells (outer ring is fully pinned).</summary>
    public double EdgePinCells { get => _edgePinCells; set => _edgePinCells = StageKnob.Finite(value, nameof(EdgePinCells)); }
    private double _edgePinCells = 2.0;
    /// <summary>Depth carved along a threaded river reach (tapered at both ends).</summary>
    public float RiverCarveDepth { get => _riverCarveDepth; set => _riverCarveDepth = StageKnob.Finite(value, nameof(RiverCarveDepth)); }
    private float _riverCarveDepth = 0.08f;
    /// <summary>Parent <see cref="ParentContext.Flow"/> at or above this threads a river.</summary>
    public float RiverFlowThreshold { get => _riverFlowThreshold; set => _riverFlowThreshold = StageKnob.Finite(value, nameof(RiverFlowThreshold)); }
    private float _riverFlowThreshold = 6f;
    /// <summary>Number of ridged-fBm octaves summed for the detail noise; more octaves add finer relief at higher cost.</summary>
    public int NoiseOctaves { get => _noiseOctaves; set => _noiseOctaves = StageKnob.NonNegative(value, nameof(NoiseOctaves)); }
    private int _noiseOctaves = 3;
    /// <summary>Ridged-Fbm octave gain (&lt;0.5 emphasizes large strike-parallel structure).</summary>
    public double DetailGain { get => _detailGain; set => _detailGain = StageKnob.Finite(value, nameof(DetailGain)); }
    private double _detailGain = 0.5;

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
