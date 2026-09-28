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
    }

    public int CellCount => Tier == SiteTier.Region ? Region!.Cells.Length : Local!.Tiles.Length;

    public float GetElevation(int cell) => Tier == SiteTier.Region ? Region!.Cells[cell].Elevation : Local!.Tiles[cell].Height;
    public BiomeType GetBiome(int cell) => Tier == SiteTier.Region ? Region!.Cells[cell].Biome : Local!.Tiles[cell].Biome;
    public bool IsWater(int cell) => Tier == SiteTier.Region ? Region!.Cells[cell].Elevation < 0f : Local!.Tiles[cell].IsWater;
    public bool IsRiverChannel(int cell) => Tier == SiteTier.Region
        ? Region!.Cells[cell].IsRiver
        : Local!.Tiles[cell].WaterDepth >= RegionMaps.LocalRiverMark;
    public byte GetDanger(int cell) => Tier == SiteTier.Region ? (byte)0 : Local!.Tiles[cell].Danger;
    public float GetMoisture(int cell) => Tier == SiteTier.Region ? Region!.Cells[cell].Moisture : Local!.Tiles[cell].Precipitation;

    public int GetAdjacent(int cell, Span<int> neighbors) => Tier == SiteTier.Region
        ? Region!.GetAdjacent(cell, neighbors)
        : Local!.GetAdjacent(cell, neighbors);
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
        var sorted = SortChecked(injectors);
        var catalog = new SiteCatalog();
        foreach (var injector in sorted)
        {
            if (injector.Tier != SiteTier.Both && injector.Tier != SiteTier.Region) continue;
            var context = new SiteInjectionContext(
                region.Address, SiteTier.Region, region.Parent, region.Bounds,
                region, null, catalog.Sites.ToArray(),
                DeriveInjectorRng(region.Address, injector.Id));
            AddProduced(catalog, injector, context);
        }
        return catalog;
    }

    public static SiteCatalog MaterializeLocal(LocalMapHandle local, IEnumerable<ISiteInjector> injectors)
    {
        ArgumentNullException.ThrowIfNull(local);
        var sorted = SortChecked(injectors);
        var catalog = new SiteCatalog();
        foreach (var injector in sorted)
        {
            if (injector.Tier != SiteTier.Both && injector.Tier != SiteTier.Local) continue;
            var context = new SiteInjectionContext(
                local.Address, SiteTier.Local, local.Parent, local.Bounds,
                null, local, catalog.Sites.ToArray(),
                DeriveInjectorRng(local.Address, injector.Id));
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
        int mixed = unchecked(address.WorldSeed * 7919
            + address.WorldTileIndex * 104729
            + (address.RegionCellIndex + 1) * 1299709
            + (address.LocalTileIndex + 1) * 15485863);
        return Rng.Create(mixed, unchecked((int)h));
    }
}

/// <summary>
/// Settles cities on the best-scoring cells, mirroring
/// <see cref="CitySiteScorer"/> weights (fresh-water +1, fertile +0.5,
/// harsh -0.5, high -0.3, danger -0.2 each, plus habitability deltas).
/// No hard freshwater exclusion: on sparse child maps dry cells still settle,
/// just lower. Existing-site deltas on a cell feed effective danger the same
/// way history modifiers feed the planet scorer.
/// </summary>
public sealed class SettlementInjector : ISiteInjector
{
    public string Id { get; }
    public int Order { get; }
    public SiteTier Tier { get; }
    public int MaxSites { get; }

    public SettlementInjector(int maxSites = 3, int order = 0, SiteTier tier = SiteTier.Both, string? id = null)
    {
        if (maxSites < 0) throw new ArgumentOutOfRangeException(nameof(maxSites));
        MaxSites = maxSites;
        Order = order;
        Tier = tier;
        Id = string.IsNullOrEmpty(id) ? "settlement" : id;
    }

    public IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scored = new List<(int Cell, double Score)>();
        Span<int> neighbors = stackalloc int[6];
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

            float dangerDelta = 0f, habDelta = 0f;
            foreach (var s in context.ExistingSites)
            {
                if (s.CellIndex != i) continue;
                dangerDelta += s.DangerDelta;
                habDelta += s.HabitabilityDelta;
            }
            int effDanger = context.GetDanger(i) + (int)MathF.Round(dangerDelta);

            double score = 1.0;
            if (water) score += 1.0;
            if (biome is BiomeType.Plains or BiomeType.Forest) score += 0.5;
            if (biome is BiomeType.Tundra or BiomeType.Desert or BiomeType.Mountain or BiomeType.Canyon) score -= 0.5;
            if (context.GetElevation(i) > 0.35f) score -= 0.3;
            score -= effDanger * 0.2;
            score += habDelta;
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
public sealed class RuinInjector : ISiteInjector
{
    public string Id { get; }
    public int Order { get; }
    public SiteTier Tier { get; }
    public int MaxSites { get; }

    public RuinInjector(int maxSites = 2, int order = 10, SiteTier tier = SiteTier.Both, string? id = null)
    {
        if (maxSites < 0) throw new ArgumentOutOfRangeException(nameof(maxSites));
        MaxSites = maxSites;
        Order = order;
        Tier = tier;
        Id = string.IsNullOrEmpty(id) ? "ruin" : id;
    }

    public IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
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
public sealed class MineInjector : ISiteInjector
{
    public string Id { get; }
    public int Order { get; }
    public SiteTier Tier { get; }
    public int MaxSites { get; }
    public DepositCatalog Deposits { get; }

    public MineInjector(DepositCatalog deposits, int maxSites = 2, int order = 20, SiteTier tier = SiteTier.Both, string? id = null)
    {
        Deposits = deposits ?? throw new ArgumentNullException(nameof(deposits));
        if (maxSites < 0) throw new ArgumentOutOfRangeException(nameof(maxSites));
        MaxSites = maxSites;
        Order = order;
        Tier = tier;
        Id = string.IsNullOrEmpty(id) ? "mine" : id;
    }

    public IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ores = new List<Deposit>(Deposits.AtTile(context.Address.WorldTileIndex));
        if (ores.Count == 0) return Array.Empty<PlacedSite>();
        ores.Sort((a, b) => a.Id.CompareTo(b.Id));

        var cells = new List<int>();
        for (int i = 0; i < context.CellCount; i++)
        {
            if (!context.IsWater(i)) cells.Add(i);
        }
        cells.Sort((a, b) =>
        {
            int c = context.GetElevation(b).CompareTo(context.GetElevation(a));
            return c != 0 ? c : a.CompareTo(b);
        });

        int take = Math.Min(Math.Min(MaxSites, ores.Count), cells.Count);
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
public sealed class LandmarkInjector : ISiteInjector
{
    public string Id { get; }
    public int Order { get; }
    public SiteTier Tier { get; }
    public int MaxSites { get; }

    public LandmarkInjector(int maxSites = 1, int order = 30, SiteTier tier = SiteTier.Both, string? id = null)
    {
        if (maxSites < 0) throw new ArgumentOutOfRangeException(nameof(maxSites));
        MaxSites = maxSites;
        Order = order;
        Tier = tier;
        Id = string.IsNullOrEmpty(id) ? "landmark" : id;
    }

    public IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cells = new List<int>();
        for (int i = 0; i < context.CellCount; i++)
        {
            if (!context.IsWater(i)) cells.Add(i);
        }
        cells.Sort((a, b) =>
        {
            int c = context.GetElevation(b).CompareTo(context.GetElevation(a));
            return c != 0 ? c : a.CompareTo(b);
        });
        int take = Math.Min(MaxSites, cells.Count);
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
