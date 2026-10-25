namespace RoguelikeToolkit.World.Core.Hydrology;

/// <summary>
/// Result of the lake water-balance solver: per-tile depths, ids, playas, and
/// the routing (hydro) surface with lakes leveled to their water surface.
/// </summary>
internal sealed class LakeSolution
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
    /// <summary>
    /// Receiver of an unponded tile of a closed lake's basin (-1 elsewhere): the next tile on its way down to the lake.
    /// The basin's routing surface is a plateau at the pour level, and resolving that flat by distance would hand part
    /// of the basin to the pour point outside it.
    /// </summary>
    public int[] BasinReceiver = Array.Empty<int>();
    /// <summary>Hops to the lake along <see cref="BasinReceiver"/> (0 elsewhere); orders the plateau donors-first.</summary>
    public int[] BasinDepth = Array.Empty<int>();
    public List<Lake> Lakes { get; } = new();
}
