namespace RoguelikeToolkit.World.Core.Sites;

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
