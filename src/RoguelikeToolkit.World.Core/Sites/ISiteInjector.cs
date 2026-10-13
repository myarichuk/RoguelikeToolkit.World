namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>One site-placement pass over a materialized region/local map.</summary>
public interface ISiteInjector
{
    /// <summary>Stable identifier. It seeds the injector's RNG substream, so changing it changes the placements it produces.</summary>
    string Id { get; }
    /// <summary>Run position: injectors run in ascending order and values must be unique within one run.</summary>
    int Order { get; }
    /// <summary>Which map tier(s) this injector runs on.</summary>
    SiteTier Tier { get; }
    /// <summary>
    /// Proposes sites for one materialized map. Must be deterministic given the context, drawing randomness only from <see cref="SiteInjectionContext.Rng"/>.
    /// </summary>
    /// <returns>Sites to add; each <see cref="PlacedSite.CellIndex"/> must be a valid cell of the context's map or the pipeline throws. Null is treated as empty.</returns>
    IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context);
}
