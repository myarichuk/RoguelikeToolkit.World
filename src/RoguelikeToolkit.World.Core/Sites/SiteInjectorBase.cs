namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>
/// Shared shell of the built-in injectors: identity, ordering, tier, and a
/// non-negative site budget. Subclasses only supply the placement rule.
/// </summary>
public abstract class SiteInjectorBase : ISiteInjector
{
    /// <summary>Injector identifier; the constructor's default id when none was given.</summary>
    public string Id { get; }
    /// <summary>Pipeline order; ascending, unique within a run.</summary>
    public int Order { get; }
    /// <summary>Tier(s) this injector runs on.</summary>
    public SiteTier Tier { get; }
    /// <summary>Upper bound on the number of sites one run places; never negative.</summary>
    public int MaxSites { get; }

    /// <summary>Initializes the shared injector state.</summary>
    /// <param name="id">Injector id; <paramref name="defaultId"/> when null or empty.</param>
    /// <param name="defaultId">Fallback id.</param>
    /// <param name="order">Pipeline order.</param>
    /// <param name="tier">Tier(s) to run on.</param>
    /// <param name="maxSites">Site budget; 0 disables placement.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSites"/> is negative.</exception>
    protected SiteInjectorBase(string? id, string defaultId, int order, SiteTier tier, int maxSites)
    {
        if (maxSites < 0) throw new ArgumentOutOfRangeException(nameof(maxSites));
        MaxSites = maxSites;
        Order = order;
        Tier = tier;
        Id = string.IsNullOrEmpty(id) ? defaultId : id;
    }

    /// <inheritdoc/>
    public abstract IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context);
}
