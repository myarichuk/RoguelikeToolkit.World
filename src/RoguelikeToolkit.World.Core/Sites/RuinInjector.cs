namespace RoguelikeToolkit.World.Core.Sites;

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
