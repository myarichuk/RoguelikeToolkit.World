namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Per-tile surface height in roughly [-1.2, 1.2]. Sea level is
/// <see cref="ElevationGenerationStage.SeaLevel"/>; lower values are ocean.
/// </summary>
public struct ElevationInfo
{
    public float Height;
}
