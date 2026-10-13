using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Hierarchy;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Sites;

namespace RoguelikeToolkit.World.Core.Features;

/// <summary>
/// Everything attached to one hex: biome, river reach with from/to neighbors,
/// water body, ranges, and glacier cover. Missing layers read as Has* = false
/// so partial pipelines stay queryable.
/// </summary>
public struct TileFeatureInfo
{
    public int TileIndex;
    public GeoCoord Coord;

    public bool HasBiome;
    public BiomeType Biome;
    public byte DangerLevel;

    public bool HasElevation;
    public float Elevation;

    public bool HasHydrology;
    public bool IsRiver;
    public float Flow;
    public int WaterBodyId;
    public float LakeDepth;

    /// <summary>Upstream river tile ("where the water comes from"), -1 at heads.</summary>
    public int UpstreamTile;
    public GeoCoord UpstreamCoord;
    /// <summary>Downstream tile ("where the water goes"), -1 in pit sinks.</summary>
    public int DownstreamTile;
    public GeoCoord DownstreamCoord;

    /// <summary>Head source of this tile's river; None for non-river tiles.</summary>
    public RiverWaterSource RiverSource;
    /// <summary>Id in <see cref="RiverCatalog"/>; -1 when unknown/absent.</summary>
    public int RiverId;
    /// <summary>Kind of the containing water body; null when unknown/absent.</summary>
    public WaterBodyKind? BodyKind;

    public bool InMountainRange;
    public bool InValley;
    public bool InCanyon;
    public bool InPlaya;
    public bool IsGlacier;
    public DepositType[] Deposits;

    /// <summary>Address of the queried tile (planet tier by default).</summary>
    public MapAddress Address;
    /// <summary>Injector-placed sites on this tile; empty when no site catalog was passed.</summary>
    public PlacedSite[] Sites;
}
