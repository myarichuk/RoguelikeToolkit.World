namespace RoguelikeToolkit.World.Core.Hydrology;

/// <summary>
/// Result of the lake water-balance solver: per-tile depths, ids, playas, and
/// the routing (hydro) surface with lakes leveled to their water surface.
/// </summary>
public sealed class LakeSolution
{
    public float[] Depth = Array.Empty<float>();
    public float[] Surface = Array.Empty<float>();
    public int[] LakeId = Array.Empty<int>();
    public bool[] IsPlaya = Array.Empty<bool>();
    /// <summary>Tiles of a closed depression (terminal lake, dry basin or closed marsh): their water has no way out except evaporation.</summary>
    public bool[] Endorheic = Array.Empty<bool>();
    /// <summary>Closed-depression tiles that hold no lake: whatever flows in evaporates in place and must not be routed on.</summary>
    public bool[] Sink = Array.Empty<bool>();
    /// <summary>Playa floors, terminal-lake tiles, and the exposed lakebed ringing them.</summary>
    public bool[] Saline = Array.Empty<bool>();
    public List<Lake> Lakes { get; } = new();
}
