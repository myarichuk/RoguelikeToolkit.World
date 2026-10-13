using RoguelikeToolkit.World.Core.Generation;
namespace RoguelikeToolkit.World.Core.Lithosphere;

/// <summary>Tuning for <see cref="IslandChainDetector"/>.</summary>
public sealed class IslandChainOptions
{
    /// <summary>
    /// A land tile is "thin" when no land tile with coast depth &gt;= this lies within
    /// <see cref="DeepReach"/> rings. Depth 1 is a tile touching water, so 4 means "at
    /// least three tiles from any shore": a ribbon is therefore at most ~6 tiles wide.
    /// </summary>
    public int DeepDepth { get => _deepDepth; set => _deepDepth = StageKnob.NonNegative(value, nameof(DeepDepth)); }
    private int _deepDepth = 4;

    /// <summary>Rings searched for an interior (deep) tile before a land tile still counts as thin.</summary>
    public int DeepReach { get => _deepReach; set => _deepReach = StageKnob.NonNegative(value, nameof(DeepReach)); }
    private int _deepReach = 2;

    /// <summary>Shortest thin run, in rings end to end, that counts as a chain; shorter ones are ordinary islets.</summary>
    public int MinLength { get => _minLength; set => _minLength = StageKnob.NonNegative(value, nameof(MinLength)); }
    private int _minLength = 6;

    /// <summary>
    /// A chain tile must be oceanic crust this many rings or fewer from a plate boundary. Natural thin land
    /// (barrier spits, the coastal strip of a continent) fails it and is never touched.
    /// </summary>
    public int BoundaryRings { get => _boundaryRings; set => _boundaryRings = StageKnob.NonNegative(value, nameof(BoundaryRings)); }
    private int _boundaryRings = 2;

    /// <summary>Bead (surviving island) length range along a broken chain, in rings.</summary>
    public int MinBead { get => _minBead; set => _minBead = StageKnob.NonNegative(value, nameof(MinBead)); }
    private int _minBead = 2;

    /// <summary>Longest surviving bead, in rings.</summary>
    public int MaxBead { get => _maxBead; set => _maxBead = StageKnob.NonNegative(value, nameof(MaxBead)); }
    private int _maxBead = 5;

    /// <summary>Strait width range between beads, in rings.</summary>
    public int MinGap { get => _minGap; set => _minGap = StageKnob.NonNegative(value, nameof(MinGap)); }
    private int _minGap = 1;

    /// <summary>Widest strait between beads, in rings.</summary>
    public int MaxGap { get => _maxGap; set => _maxGap = StageKnob.NonNegative(value, nameof(MaxGap)); }
    private int _maxGap = 3;

    /// <summary>
    /// Grids with fewer tiles than this are left alone (default: size 5 and up). On a coarse grid a tile is
    /// 400+ km across, so a ribbon one or two tiles wide is already the smallest land feature the grid can
    /// express and cutting it only deletes land (size 3 loses a tenth of it, polar tundra included).
    /// </summary>
    public int MinTileCount { get => _minTileCount; set => _minTileCount = StageKnob.NonNegative(value, nameof(MinTileCount)); }
    private int _minTileCount = 8000;

    /// <summary>Height a drowned strait tile is pushed down to, below sea level.</summary>
    public double GapDepth { get => _gapDepth; set => _gapDepth = StageKnob.Finite(value, nameof(GapDepth)); }
    private double _gapDepth = 0.06;
}
