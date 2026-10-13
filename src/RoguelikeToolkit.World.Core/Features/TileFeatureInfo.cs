using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Lithosphere;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Sites;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Features;

/// <summary>
/// Everything attached to one hex: biome, river reach with from/to neighbors,
/// water body, ranges, and glacier cover. Missing layers read as Has* = false
/// so partial pipelines stay queryable.
/// </summary>
public struct TileFeatureInfo
{
    /// <summary>Index of the queried tile in the <see cref="Storage.WorldDataStore"/>.</summary>
    public int TileIndex;
    /// <summary>Centre of the tile; latitude and longitude in degrees.</summary>
    public GeoCoord Coord;

    /// <summary>True when the biome layer is registered or a tile override supplied a biome; <see cref="Biome"/> is meaningful only then.</summary>
    public bool HasBiome;
    /// <summary>The tile's biome (after any override); default when <see cref="HasBiome"/> is false.</summary>
    public BiomeType Biome;
    /// <summary>Danger rating 0 to 255 from the biome layer, replaced by a tile override's value (clamped to that range); 0 when neither supplies one.</summary>
    public byte DangerLevel;

    /// <summary>True when the elevation layer is registered; <see cref="Elevation"/> and <see cref="IsGlacier"/> are meaningful only then.</summary>
    public bool HasElevation;
    /// <summary>Surface height (<c>ElevationInfo.Height</c>): roughly -1.2 to 1.2 with sea level at 0, so negative is sea floor.</summary>
    public float Elevation;

    /// <summary>True when the hydrology layer is registered; the water fields below are meaningful only then.</summary>
    public bool HasHydrology;
    /// <summary>A river channel passes through this tile. Distinct from <see cref="RiverId"/>: a cataloged river path also includes its terminal water tile, which is not a river tile.</summary>
    public bool IsRiver;
    /// <summary>Discharge-weighted flow accumulation through the tile, in <see cref="Hydrology.HydrologyInfo.Flow"/> units.</summary>
    public float Flow;
    /// <summary>Id of the lake, sea or ocean covering this tile; -1 when the tile is in no water body or hydrology is missing.</summary>
    public int WaterBodyId;
    /// <summary>Depth of water ponded on the tile; 0 on dry land and on sea tiles.</summary>
    public float LakeDepth;

    /// <summary>Upstream river tile ("where the water comes from"), -1 at heads.</summary>
    public int UpstreamTile;
    /// <summary>Centre of <see cref="UpstreamTile"/>; the default coordinate (0, 0) when that is -1.</summary>
    public GeoCoord UpstreamCoord;
    /// <summary>Downstream tile ("where the water goes"), -1 in pit sinks.</summary>
    public int DownstreamTile;
    /// <summary>Centre of <see cref="DownstreamTile"/>; the default coordinate (0, 0) when that is -1.</summary>
    public GeoCoord DownstreamCoord;

    /// <summary>Head source of this tile's river; None for non-river tiles.</summary>
    public RiverWaterSource RiverSource;
    /// <summary>Id in <see cref="RiverCatalog"/>; -1 when unknown/absent.</summary>
    public int RiverId;
    /// <summary>Kind of the containing water body; null when unknown/absent.</summary>
    public WaterBodyKind? BodyKind;

    /// <summary>The tile belongs to a <see cref="RangeKind.MountainRange"/> feature of the supplied catalog; false when no range catalog was passed.</summary>
    public bool InMountainRange;
    /// <summary>The tile belongs to a <see cref="RangeKind.Valley"/> feature of the supplied catalog; false when no range catalog was passed.</summary>
    public bool InValley;
    /// <summary>The tile belongs to a <see cref="RangeKind.Canyon"/> feature of the supplied catalog; false when no range catalog was passed.</summary>
    public bool InCanyon;
    /// <summary>The tile belongs to a <see cref="RangeKind.Playa"/> feature of the supplied catalog; false when no range catalog was passed.</summary>
    public bool InPlaya;
    /// <summary>The tile is glaciated (see <see cref="Climate.Glaciology"/>; temperature and precipitation are used when the climate layer exists). Always false when <see cref="HasElevation"/> is false.</summary>
    public bool IsGlacier;
    /// <summary>Types of the deposits on this tile, in catalog order; empty when none or no deposit catalog was passed.</summary>
    public DepositType[] Deposits;

    /// <summary>Address of the queried tile (planet tier by default).</summary>
    public MapAddress Address;
    /// <summary>Injector-placed sites on this tile; empty when no site catalog was passed.</summary>
    public PlacedSite[] Sites;
}
