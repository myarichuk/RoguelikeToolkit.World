namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>Marks the highest land cell as a landmark.</summary>
public sealed class LandmarkInjector : SiteInjectorBase
{
    /// <summary>Creates a landmark injector.</summary>
    /// <param name="maxSites">Maximum landmarks to place; must not be negative.</param>
    /// <param name="order">Pipeline order.</param>
    /// <param name="tier">Tier(s) to run on.</param>
    /// <param name="id">Injector id; "landmark" when null or empty.</param>
    public LandmarkInjector(int maxSites = 1, int order = 30, SiteTier tier = SiteTier.Both, string? id = null)
        : base(id, "landmark", order, tier, maxSites) { }

    /// <inheritdoc/>
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
