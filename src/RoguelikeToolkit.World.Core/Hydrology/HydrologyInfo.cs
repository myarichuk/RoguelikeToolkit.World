using RoguelikeToolkit.World.Core.Features;

namespace RoguelikeToolkit.World.Core.Hydrology;

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
    /// <summary>Discharge accumulated at this tile (the runoff of everything draining through it, in model runoff units); a tile is a river once it clears the channel threshold.</summary>
    public float Flow;
    /// <summary>Id of the water body (ocean, sea or lake) this tile belongs to, matching <see cref="WaterBodyCatalog"/>; -1 for none.</summary>
    public int WaterBodyId;
    /// <summary>1 if a river channel passes through the tile, else 0. Never set on ocean or ponded-lake tiles.</summary>
    public byte IsRiver;
    /// <summary>Routing surface height: the bed height on land, levelled to the water surface across a lake.</summary>
    public float Surface;
    /// <summary>Depth of water ponded over the tile in a lake (water surface minus bed); 0 on dry land and at sea.</summary>
    public float LakeDepth;
    /// <summary>1 on the floor of a dry closed basin (a salt flat or playa), else 0.</summary>
    public byte IsPlaya;
    /// <summary>1 when the tile's water has no path to the sea: it ends in a closed lake, a dry basin, or an inland sink.</summary>
    public byte Endorheic;
    /// <summary>1 on a playa, a terminal (closed) lake, and the exposed lakebed ringing one: ground where evaporation leaves salt.</summary>
    public byte Saline;
}
