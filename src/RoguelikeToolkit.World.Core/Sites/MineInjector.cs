using RoguelikeToolkit.World.Core.Lithosphere;

namespace RoguelikeToolkit.World.Core.Sites;

/// <summary>
/// Places mines for planet deposits on the parent world tile, favoring high
/// ground. Empty when the <see cref="DepositCatalog"/> holds nothing there.
/// </summary>
public sealed class MineInjector : SiteInjectorBase
{
    /// <summary>The planet deposit catalog consulted for the parent world tile.</summary>
    public DepositCatalog Deposits { get; }

    /// <summary>Creates a mine injector.</summary>
    /// <param name="deposits">Planet deposit catalog.</param>
    /// <param name="maxSites">Maximum mines to place; must not be negative.</param>
    /// <param name="order">Pipeline order.</param>
    /// <param name="tier">Tier(s) to run on.</param>
    /// <param name="id">Injector id; "mine" when null or empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="deposits"/> is null.</exception>
    public MineInjector(DepositCatalog deposits, int maxSites = 2, int order = 20, SiteTier tier = SiteTier.Both, string? id = null)
        : base(id, "mine", order, tier, maxSites)
    {
        Deposits = deposits ?? throw new ArgumentNullException(nameof(deposits));
    }

    /// <inheritdoc/>
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
