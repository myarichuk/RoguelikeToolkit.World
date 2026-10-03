using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Tier a site injector applies to.</summary>
public enum SiteTier : byte
{
    Region = 0,
    Local = 1,
    Both = 2,
}

/// <summary>Kind of a placed site.</summary>
public enum SiteKind : byte
{
    Ruin = 0,
    City = 1,
    Mine = 2,
    Landmark = 3,
}

/// <summary>
/// One injector-placed site on a region/local map. CellIndex addresses the
/// materialized tier map (region cell or local tile). NameSeed is an opaque
/// draw from the injector's dedicated stream for game-side name generation.
/// Tags are free-form labels (e.g. "mine", "iron", "ancient").
/// </summary>
public sealed class PlacedSite
{
    public SiteKind Kind { get; init; }
    public int CellIndex { get; init; }
    /// <summary>Alias for <see cref="CellIndex"/> (local-tile indexing).</summary>
    public int TileIndex => CellIndex;
    public int FootprintRadius { get; init; }
    public uint NameSeed { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public float DangerDelta { get; init; }
    public float HabitabilityDelta { get; init; }
    public string InjectorId { get; init; } = string.Empty;

    public PlacedSite WithInjector(string injectorId) => new()
    {
        Kind = Kind,
        CellIndex = CellIndex,
        FootprintRadius = FootprintRadius,
        NameSeed = NameSeed,
        Tags = Tags,
        DangerDelta = DangerDelta,
        HabitabilityDelta = HabitabilityDelta,
        InjectorId = injectorId,
    };
}

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

/// <summary>Read-only per-cell view of one materialized tier map; removes the region/local branch from every accessor.</summary>
internal interface ITierSurface
{
    int CellCount { get; }
    float GetElevation(int cell);
    BiomeType GetBiome(int cell);
    bool IsWater(int cell);
    bool IsRiverChannel(int cell);
    byte GetDanger(int cell);
    float GetMoisture(int cell);
    int GetAdjacent(int cell, Span<int> neighbors);
}

internal sealed class RegionSurface(RegionHandle region) : ITierSurface
{
    public int CellCount => region.Cells.Length;
    public float GetElevation(int cell) => region.Cells[cell].Elevation;
    public BiomeType GetBiome(int cell) => region.Cells[cell].Biome;
    public bool IsWater(int cell) => region.Cells[cell].Elevation < 0f;
    public bool IsRiverChannel(int cell) => region.Cells[cell].IsRiver;
    public byte GetDanger(int cell) => 0;
    public float GetMoisture(int cell) => region.Cells[cell].Moisture;
    public int GetAdjacent(int cell, Span<int> neighbors) => region.GetAdjacent(cell, neighbors);
}

internal sealed class LocalSurface(LocalMapHandle local) : ITierSurface
{
    public int CellCount => local.Tiles.Length;
    public float GetElevation(int cell) => local.Tiles[cell].Height;
    public BiomeType GetBiome(int cell) => local.Tiles[cell].Biome;
    public bool IsWater(int cell) => local.Tiles[cell].IsWater;
    public bool IsRiverChannel(int cell) => local.Tiles[cell].WaterDepth >= RegionMaps.LocalRiverMark;
    public byte GetDanger(int cell) => local.Tiles[cell].Danger;
    public float GetMoisture(int cell) => local.Tiles[cell].Precipitation;
    public int GetAdjacent(int cell, Span<int> neighbors) => local.GetAdjacent(cell, neighbors);
}

/// <summary>One site-placement pass over a materialized region/local map.</summary>
public interface ISiteInjector
{
    string Id { get; }
    int Order { get; }
    SiteTier Tier { get; }
    IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context);
}

/// <summary>Managed sparse store for injector-placed sites (overlay, not a dense layer).</summary>
public sealed class SiteCatalog
{
    public List<PlacedSite> Sites { get; } = new();

    public void Add(PlacedSite site)
    {
        ArgumentNullException.ThrowIfNull(site);
        Sites.Add(site);
    }

    public IReadOnlyList<PlacedSite> AtCell(int cellIndex)
    {
        var found = new List<PlacedSite>();
        foreach (var s in Sites)
        {
            if (s.CellIndex == cellIndex) found.Add(s);
        }
        return found;
    }

    public IReadOnlyList<PlacedSite> OfKind(SiteKind kind)
    {
        var found = new List<PlacedSite>();
        foreach (var s in Sites)
        {
            if (s.Kind == kind) found.Add(s);
        }
        return found;
    }

    /// <summary>Summed danger/habitability deltas on one cell.</summary>
    public (float DangerDelta, float HabitabilityDelta) GetCellModifier(int cellIndex)
    {
        float danger = 0f, hab = 0f;
        foreach (var s in Sites)
        {
            if (s.CellIndex != cellIndex) continue;
            danger += s.DangerDelta;
            hab += s.HabitabilityDelta;
        }
        return (danger, hab);
    }
}

/// <summary>
/// Order-based site injector pipeline shared by the region/local handle
/// materializers. Injectors run ascending by <see cref="ISiteInjector.Order"/>;
/// duplicate Order values throw; each injector sees earlier output via
/// <see cref="SiteInjectionContext.ExistingSites"/> and draws from its own
/// (injector, address) RNG substream, so appending a later injector never
/// reshuffles earlier sites.
/// </summary>
public static class SiteInjectorPipeline
{
    public static SiteCatalog MaterializeRegion(RegionHandle region, IEnumerable<ISiteInjector> injectors)
    {
        ArgumentNullException.ThrowIfNull(region);
        return Materialize(region.Address, SiteTier.Region, injectors, (existing, rng) =>
            new SiteInjectionContext(
                region.Address, SiteTier.Region, region.Parent, region.Bounds,
                region, null, existing, rng));
    }

    public static SiteCatalog MaterializeLocal(LocalMapHandle local, IEnumerable<ISiteInjector> injectors)
    {
        ArgumentNullException.ThrowIfNull(local);
        return Materialize(local.Address, SiteTier.Local, injectors, (existing, rng) =>
            new SiteInjectionContext(
                local.Address, SiteTier.Local, local.Parent, local.Bounds,
                null, local, existing, rng));
    }

    private static SiteCatalog Materialize(
        MapAddress address, SiteTier tier, IEnumerable<ISiteInjector> injectors,
        Func<IReadOnlyList<PlacedSite>, Rng, SiteInjectionContext> makeContext)
    {
        var sorted = SortChecked(injectors);
        var catalog = new SiteCatalog();
        foreach (var injector in sorted)
        {
            if (injector.Tier != SiteTier.Both && injector.Tier != tier) continue;
            var context = makeContext(catalog.Sites.ToArray(), DeriveInjectorRng(address, injector.Id));
            AddProduced(catalog, injector, context);
        }
        return catalog;
    }

    private static List<ISiteInjector> SortChecked(IEnumerable<ISiteInjector> injectors)
    {
        ArgumentNullException.ThrowIfNull(injectors);
        var sorted = new List<ISiteInjector>(injectors);
        if (sorted.Contains(null!)) throw new ArgumentException("Injector list contains null.", nameof(injectors));
        sorted.Sort((a, b) => a.Order.CompareTo(b.Order));
        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].Order == sorted[i - 1].Order)
                throw new InvalidOperationException(
                    $"Duplicate site injector Order {sorted[i].Order} ('{sorted[i - 1].Id}' and '{sorted[i].Id}').");
        }
        return sorted;
    }

    private static void AddProduced(SiteCatalog catalog, ISiteInjector injector, SiteInjectionContext context)
    {
        var produced = injector.Inject(context) ?? Array.Empty<PlacedSite>();
        foreach (var site in produced)
        {
            if (site == null) throw new InvalidOperationException($"Injector '{injector.Id}' produced a null site.");
            if ((uint)site.CellIndex >= (uint)context.CellCount)
                throw new InvalidOperationException($"Injector '{injector.Id}' placed site off-map at cell {site.CellIndex}.");
            catalog.Add(site.InjectorId.Length == 0 ? site.WithInjector(injector.Id) : site);
        }
    }

    /// <summary>Independent substream per (injector, address); stable FNV-1a id hash.</summary>
    public static Rng DeriveInjectorRng(MapAddress address, string injectorId)
    {
        uint h = 2166136261u;
        foreach (char c in injectorId ?? string.Empty)
        {
            h ^= c;
            h *= 16777619u;
        }
        // Fold the address in one Derive step per component (no linear pre-mix that
        // distinct (tile, cell, local) triples could cancel).
        return Rng.Create(address.WorldSeed, unchecked((int)h))
            .Derive(address.WorldTileIndex)
            .Derive(address.RegionCellIndex + 1)
            .Derive(address.LocalTileIndex + 1);
    }
}

/// <summary>
/// Shared shell of the built-in injectors: identity, ordering, tier, and a
/// non-negative site budget. Subclasses only supply the placement rule.
/// </summary>
public abstract class SiteInjectorBase : ISiteInjector
{
    public string Id { get; }
    public int Order { get; }
    public SiteTier Tier { get; }
    public int MaxSites { get; }

    protected SiteInjectorBase(string? id, string defaultId, int order, SiteTier tier, int maxSites)
    {
        if (maxSites < 0) throw new ArgumentOutOfRangeException(nameof(maxSites));
        MaxSites = maxSites;
        Order = order;
        Tier = tier;
        Id = string.IsNullOrEmpty(id) ? defaultId : id;
    }

    public abstract IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context);
}

/// <summary>
/// Settles cities on the best-scoring cells, mirroring
/// <see cref="CitySiteScorer"/> weights (fresh-water +1, fertile +0.5,
/// harsh -0.5, high -0.3, danger -0.2 each, plus habitability deltas).
/// No hard freshwater exclusion: on sparse child maps dry cells still settle,
/// just lower. Existing-site deltas on a cell feed effective danger the same
/// way history modifiers feed the planet scorer.
/// </summary>
public sealed class SettlementInjector : SiteInjectorBase
{
    public SettlementInjector(int maxSites = 3, int order = 0, SiteTier tier = SiteTier.Both, string? id = null)
        : base(id, "settlement", order, tier, maxSites) { }

    public override IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scored = new List<(int Cell, double Score)>();
        Span<int> neighbors = stackalloc int[6];

        // Existing-site deltas per cell, summed in site order (one pass, not cells x sites).
        var deltas = new Dictionary<int, (float Danger, float Hab)>();
        foreach (var s in context.ExistingSites)
        {
            deltas.TryGetValue(s.CellIndex, out var d);
            deltas[s.CellIndex] = (d.Danger + s.DangerDelta, d.Hab + s.HabitabilityDelta);
        }
        for (int i = 0; i < context.CellCount; i++)
        {
            if (context.IsWater(i)) continue;
            var biome = context.GetBiome(i);
            if (biome is BiomeType.Ocean or BiomeType.Glacier) continue;

            bool water = context.IsRiverChannel(i);
            if (!water)
            {
                int adjacent = context.GetAdjacent(i, neighbors);
                for (int k = 0; k < adjacent && !water; k++)
                    water = context.IsWater(neighbors[k]) || context.IsRiverChannel(neighbors[k]);
            }

            deltas.TryGetValue(i, out var delta);
            var (dangerDelta, habDelta) = delta;
            int effDanger = context.GetDanger(i) + (int)MathF.Round(dangerDelta);

            double score = SettlementScoring.Score(water, biome, context.GetElevation(i), effDanger, habDelta);
            scored.Add((i, score));
        }
        scored.Sort((a, b) =>
        {
            int c = b.Score.CompareTo(a.Score);
            return c != 0 ? c : a.Cell.CompareTo(b.Cell);
        });
        int take = Math.Min(MaxSites, scored.Count);
        var result = new List<PlacedSite>(take);
        for (int k = 0; k < take; k++)
        {
            result.Add(new PlacedSite
            {
                Kind = SiteKind.City,
                CellIndex = scored[k].Cell,
                FootprintRadius = 1,
                NameSeed = context.Rng.NextUInt(),
                Tags = new[] { "settlement" },
                HabitabilityDelta = 0.2f,
                InjectorId = Id,
            });
        }
        return result;
    }
}

/// <summary>Scatters ruins on random land cells via the injector's own stream.</summary>
public sealed class RuinInjector : SiteInjectorBase
{
    public RuinInjector(int maxSites = 2, int order = 10, SiteTier tier = SiteTier.Both, string? id = null)
        : base(id, "ruin", order, tier, maxSites) { }

    public override IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var candidates = new List<int>();
        for (int i = 0; i < context.CellCount; i++)
        {
            if (!context.IsWater(i)) candidates.Add(i);
        }
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = (int)context.Rng.NextUInt((uint)(i + 1));
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        int take = Math.Min(MaxSites, candidates.Count);
        var result = new List<PlacedSite>(take);
        for (int k = 0; k < take; k++)
        {
            result.Add(new PlacedSite
            {
                Kind = SiteKind.Ruin,
                CellIndex = candidates[k],
                FootprintRadius = 1,
                NameSeed = context.Rng.NextUInt(),
                Tags = new[] { "ruin", "ancient" },
                DangerDelta = 1f,
                HabitabilityDelta = 0.1f,
                InjectorId = Id,
            });
        }
        return result;
    }
}

/// <summary>
/// Places mines for planet deposits on the parent world tile, favoring high
/// ground. Empty when the <see cref="DepositCatalog"/> holds nothing there.
/// </summary>
public sealed class MineInjector : SiteInjectorBase
{
    public DepositCatalog Deposits { get; }

    public MineInjector(DepositCatalog deposits, int maxSites = 2, int order = 20, SiteTier tier = SiteTier.Both, string? id = null)
        : base(id, "mine", order, tier, maxSites)
    {
        Deposits = deposits ?? throw new ArgumentNullException(nameof(deposits));
    }

    public override IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ores = new List<Deposit>(Deposits.AtTile(context.Address.WorldTileIndex));
        if (ores.Count == 0) return Array.Empty<PlacedSite>();
        ores.Sort((a, b) => a.Id.CompareTo(b.Id));

        var cells = context.LandCellsByElevationDescending();

        int take = Math.Min(Math.Min(MaxSites, ores.Count), cells.Length);
        var result = new List<PlacedSite>(take);
        for (int k = 0; k < take; k++)
        {
            result.Add(new PlacedSite
            {
                Kind = SiteKind.Mine,
                CellIndex = cells[k],
                FootprintRadius = 1,
                NameSeed = context.Rng.NextUInt(),
                Tags = new[] { "mine", ores[k].Type.ToString().ToLowerInvariant() },
                DangerDelta = 1f,
                InjectorId = Id,
            });
        }
        return result;
    }
}

/// <summary>Marks the highest land cell as a landmark.</summary>
public sealed class LandmarkInjector : SiteInjectorBase
{
    public LandmarkInjector(int maxSites = 1, int order = 30, SiteTier tier = SiteTier.Both, string? id = null)
        : base(id, "landmark", order, tier, maxSites) { }

    public override IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cells = context.LandCellsByElevationDescending();
        int take = Math.Min(MaxSites, cells.Length);
        var result = new List<PlacedSite>(take);
        for (int k = 0; k < take; k++)
        {
            result.Add(new PlacedSite
            {
                Kind = SiteKind.Landmark,
                CellIndex = cells[k],
                FootprintRadius = 0,
                NameSeed = context.Rng.NextUInt(),
                Tags = new[] { "landmark" },
                InjectorId = Id,
            });
        }
        return result;
    }
}
