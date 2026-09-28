namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Parent-tile summary consumed when deriving a child map: scalar means over
/// the tile plus its neighbor ring, plus orientation vectors that let the
/// child align ridges, drainage, and winds with the parent surface.
/// </summary>
public sealed class ParentContext
{
    // Scalar means (tile + ring).
    public float MeanElevation { get; init; }
    public BiomeType DominantBiome { get; init; } = BiomeType.Plains;
    public float MeanTemperature { get; init; } = 0.5f;
    public float MeanPrecipitation { get; init; } = 0.5f;
    /// <summary>Moisture mean; currently the precipitation proxy (no dense moisture layer exists).</summary>
    public float MeanMoisture { get; init; } = 0.5f;

    // Orientation.
    /// <summary>Least-squares elevation gradient, tangent to the sphere at the tile center.</summary>
    public Vector3D ElevationGradient { get; init; }
    /// <summary>Bearing of the downhill direction, clockwise from north, in [-pi, pi]. 0 when flat.</summary>
    public double AspectRadians { get; init; }
    /// <summary>Unit tangent vector along the orogeny strike (ridge/contour direction).</summary>
    public Vector3D OrogenyStrike { get; init; } = new Vector3D(1, 0, 0);
    /// <summary>Per-neighbor heights in <see cref="WorldDataStore.GetAdjacent"/> order.</summary>
    public float[] NeighborHeights { get; init; } = Array.Empty<float>();
    /// <summary>Neighbor tile index where surface flow enters (highest neighbor), -1 when unknown.</summary>
    public int FlowEntryTile { get; init; } = -1;
    /// <summary>Neighbor tile index where surface flow exits (lowest lower neighbor), -1 for sinks.</summary>
    public int FlowExitTile { get; init; } = -1;
    /// <summary>Mean prevailing wind over tile + ring, normalized when nonzero.</summary>
    public Vector3D Wind { get; init; }
    /// <summary>Whether the parent tile carries a river channel.</summary>
    public bool IsRiver { get; init; }
    /// <summary>Parent discharge (flow accumulation units); high values thread a river even without a channel flag.</summary>
    public float Flow { get; init; }
}
