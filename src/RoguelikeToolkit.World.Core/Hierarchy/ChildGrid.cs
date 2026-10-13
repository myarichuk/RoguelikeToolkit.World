namespace RoguelikeToolkit.World.Core.Hierarchy;

/// <summary>
/// Per-cell surface of one derived child map, before it is projected into
/// <see cref="RegionCell"/>s or <see cref="LocalTile"/>s.
/// </summary>
internal sealed class ChildGrid
{
    public int Size;
    public int Count;
    public float[] Heights = Array.Empty<float>();
    public double[] Jitter = Array.Empty<double>();
    public bool[] IsRiver = Array.Empty<bool>();
    public float[] WaterDepth = Array.Empty<float>();
    public float[] Flow = Array.Empty<float>();
    public int RiverEntry = -1;
    public int RiverExit = -1;

    // Planar hex-lattice position (x, y) and border flag per cell.
    public double[] X = Array.Empty<double>();
    public double[] Y = Array.Empty<double>();
    public bool[] IsEdge = Array.Empty<bool>();
}
