using RoguelikeToolkit.World.Core.Hierarchy;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Sites;

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
    /// <summary>Runs the injectors that apply to the region tier over a region map and returns the resulting catalog.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> or <paramref name="injectors"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="injectors"/> contains a null entry.</exception>
    /// <exception cref="InvalidOperationException">Two injectors share an Order (checked before tier filtering), or an injector returns a null site or an off-map cell.</exception>
    public static SiteCatalog MaterializeRegion(RegionHandle region, IEnumerable<ISiteInjector> injectors)
    {
        ArgumentNullException.ThrowIfNull(region);
        return Materialize(region.Address, SiteTier.Region, injectors, (existing, rng) =>
            new SiteInjectionContext(
                region.Address, SiteTier.Region, region.Parent, region.Bounds,
                region, null, existing, rng));
    }

    /// <summary>Runs the injectors that apply to the local tier over a local map and returns the resulting catalog.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="local"/> or <paramref name="injectors"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="injectors"/> contains a null entry.</exception>
    /// <exception cref="InvalidOperationException">Two injectors share an Order (checked before tier filtering), or an injector returns a null site or an off-map cell.</exception>
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
