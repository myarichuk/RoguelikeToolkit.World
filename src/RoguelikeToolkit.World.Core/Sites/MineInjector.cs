using RoguelikeToolkit.World.Core.Lithosphere;

namespace RoguelikeToolkit.World.Core.Sites;

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
