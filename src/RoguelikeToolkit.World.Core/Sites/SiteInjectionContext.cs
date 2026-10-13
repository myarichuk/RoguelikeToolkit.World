using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Hierarchy;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>
/// Input for one injector run: where the map lives, the tier map itself,
/// accumulated sites from earlier injectors, and a dedicated RNG substream
/// derived from (injector id, address) so injectors never share state.
/// </summary>
public sealed class SiteInjectionContext
{
    /// <summary>Address of the map being materialized.</summary>
    public MapAddress Address { get; }
    /// <summary>Tier of the map being materialized: always <see cref="SiteTier.Region"/> or <see cref="SiteTier.Local"/>.</summary>
    public SiteTier Tier { get; }
    /// <summary>Parent-tile summary the map was derived from.</summary>
    public ParentContext Parent { get; }
    /// <summary>Extent and border-ring cells of the map.</summary>
    public MapBounds Bounds { get; }
    /// <summary>The region map when <see cref="Tier"/> is region; otherwise null.</summary>
    public RegionHandle? Region { get; }
    /// <summary>The local map when <see cref="Tier"/> is local; otherwise null.</summary>
    public LocalMapHandle? Local { get; }
    /// <summary>Sites placed by earlier injectors (Order-sorted run-up).</summary>
    public IReadOnlyList<PlacedSite> ExistingSites { get; }

    // Public field (not a property): Rng is a mutating struct, so a property
    // getter would hand out a copy and silently drop every draw.
    /// <summary>This injector's dedicated RNG substream. Draw from it in place; it is a mutable struct, so copying it forks the stream.</summary>
    public Rng Rng;

    /// <summary>Creates a context over one materialized tier map.</summary>
    /// <param name="address">Address of the map.</param>
    /// <param name="tier">Tier to materialize; must be <see cref="SiteTier.Region"/> or <see cref="SiteTier.Local"/>.</param>
    /// <param name="parent">Parent context of the map.</param>
    /// <param name="bounds">Bounds of the map.</param>
    /// <param name="region">Region map; required for the region tier.</param>
    /// <param name="local">Local map; required for the local tier.</param>
    /// <param name="existingSites">Sites placed by earlier injectors.</param>
    /// <param name="rng">The injector's RNG substream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="existingSites"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="tier"/> is <see cref="SiteTier.Both"/>, or the handle for the tier is null.</exception>
    public SiteInjectionContext(
        MapAddress address,
        SiteTier tier,
        ParentContext parent,
        MapBounds bounds,
        RegionHandle? region,
        LocalMapHandle? local,
        IReadOnlyList<PlacedSite> existingSites,
        Rng rng)
    {
        Address = address;
        Tier = tier;
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        Bounds = bounds;
        Region = region;
        Local = local;
        ExistingSites = existingSites ?? throw new ArgumentNullException(nameof(existingSites));
        Rng = rng;
        if (tier == SiteTier.Region && region == null) throw new ArgumentException("Region tier needs a region handle.", nameof(region));
        if (tier == SiteTier.Local && local == null) throw new ArgumentException("Local tier needs a local handle.", nameof(local));
        if (tier == SiteTier.Both) throw new ArgumentException("Context materializes one tier.", nameof(tier));
        _surface = tier == SiteTier.Region ? new RegionSurface(region!) : new LocalSurface(local!);
    }

    private readonly ITierSurface _surface;

    /// <summary>Number of cells in the tier map.</summary>
    public int CellCount => _surface.CellCount;
    /// <summary>Elevation of a cell, in planet elevation units where 0 is sea level.</summary>
    public float GetElevation(int cell) => _surface.GetElevation(cell);
    /// <summary>Biome of a cell.</summary>
    public BiomeType GetBiome(int cell) => _surface.GetBiome(cell);
    /// <summary>Whether a cell is water: below sea level (region) or flagged water (local).</summary>
    public bool IsWater(int cell) => _surface.IsWater(cell);
    /// <summary>Whether a cell carries a river channel: the river flag (region) or water depth at least <see cref="RegionMaps.LocalRiverMark"/> (local).</summary>
    public bool IsRiverChannel(int cell) => _surface.IsRiverChannel(cell);
    /// <summary>Danger level of a cell. Region cells carry no danger data, so this is always 0 on the region tier.</summary>
    public byte GetDanger(int cell) => _surface.GetDanger(cell);
    /// <summary>Moisture of a cell in [0, 1]: region moisture, or precipitation on the local tier.</summary>
    public float GetMoisture(int cell) => _surface.GetMoisture(cell);
    /// <summary>Writes the hex neighbors of a cell into <paramref name="neighbors"/> and returns the count; see <see cref="GridMapHandle{TTile}.GetAdjacent"/>.</summary>
    public int GetAdjacent(int cell, Span<int> neighbors) => _surface.GetAdjacent(cell, neighbors);

    /// <summary>
    /// Land (non-water) cells from highest to lowest elevation; equal
    /// elevations resolve to the lowest cell index.
    /// </summary>
    public int[] LandCellsByElevationDescending()
    {
        int n = CellCount;
        var elevations = new float[n];
        for (int i = 0; i < n; i++) elevations[i] = GetElevation(i);
        var order = TileOrdering.DescendingByValue(elevations);
        var land = new List<int>(n);
        foreach (int i in order)
            if (!IsWater(i)) land.Add(i);
        return land.ToArray();
    }
}
