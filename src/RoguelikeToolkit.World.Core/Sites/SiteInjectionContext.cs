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
    public MapAddress Address { get; }
    public SiteTier Tier { get; }
    public ParentContext Parent { get; }
    public MapBounds Bounds { get; }
    public RegionHandle? Region { get; }
    public LocalMapHandle? Local { get; }
    /// <summary>Sites placed by earlier injectors (Order-sorted run-up).</summary>
    public IReadOnlyList<PlacedSite> ExistingSites { get; }

    // Public field (not a property): Rng is a mutating struct, so a property
    // getter would hand out a copy and silently drop every draw.
    public Rng Rng;

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

    public int CellCount => _surface.CellCount;
    public float GetElevation(int cell) => _surface.GetElevation(cell);
    public BiomeType GetBiome(int cell) => _surface.GetBiome(cell);
    public bool IsWater(int cell) => _surface.IsWater(cell);
    public bool IsRiverChannel(int cell) => _surface.IsRiverChannel(cell);
    public byte GetDanger(int cell) => _surface.GetDanger(cell);
    public float GetMoisture(int cell) => _surface.GetMoisture(cell);
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
