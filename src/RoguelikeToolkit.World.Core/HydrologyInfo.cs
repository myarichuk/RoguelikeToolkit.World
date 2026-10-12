using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Dense per-tile hydrology field derived from elevation: discharge-weighted
/// flow accumulation routed on the filled surface, the water-body id this tile
/// belongs to (-1 = none, lakes included), whether a river channel passes
/// through, the routing surface (bed height, leveled to the water surface on
/// lakes), ponded lake depth (0 = dry land or sea), and whether the tile is
/// a dry basin floor (playa/salt flat), whether the tile drains to an inland
/// sink instead of the sea (endorheic), and whether it holds salt (a playa, a
/// terminal lake, or the exposed margin of one).
/// Sparse geometry (river polylines, lake polygons) lives in
/// <see cref="RiverCatalog"/> / <see cref="WaterBodyCatalog"/>.
/// </summary>
public struct HydrologyInfo
{
    public float Flow;
    public int WaterBodyId;
    public byte IsRiver;
    public float Surface;
    public float LakeDepth;
    public byte IsPlaya;
    /// <summary>1 when the tile's water has no path to the sea: it ends in a closed lake, a dry basin, or an inland sink.</summary>
    public byte Endorheic;
    /// <summary>1 on a playa, a terminal (closed) lake, and the exposed lakebed ringing one: ground where evaporation leaves salt.</summary>
    public byte Saline;
}

public class HydrologyLayer : StoreLayer<HydrologyInfo>
{
    public HydrologyLayer(WorldDataStore store) : base(store) { }
}
