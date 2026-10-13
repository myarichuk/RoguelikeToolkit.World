namespace RoguelikeToolkit.World.Core.Elevation;

/// <summary>
/// Per-tile surface height in roughly [-1.2, 1.2]. Sea level is
/// <see cref="ElevationGenerationStage.SeaLevel"/>; lower values are ocean.
/// </summary>
public struct ElevationInfo
{
    /// <summary>Surface height in model height units: sea level is <see cref="ElevationGenerationStage.SeaLevel"/> (0) and lower is below it. Erosion refines it in place.</summary>
    public float Height;
}
