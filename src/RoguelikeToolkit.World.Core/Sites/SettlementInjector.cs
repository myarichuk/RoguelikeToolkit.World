using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Settlement;

namespace RoguelikeToolkit.World.Core.Sites;

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
    /// <summary>Creates a settlement injector.</summary>
    /// <param name="maxSites">Maximum cities to place; must not be negative.</param>
    /// <param name="order">Pipeline order.</param>
    /// <param name="tier">Tier(s) to run on.</param>
    /// <param name="id">Injector id; "settlement" when null or empty.</param>
    public SettlementInjector(int maxSites = 3, int order = 0, SiteTier tier = SiteTier.Both, string? id = null)
        : base(id, "settlement", order, tier, maxSites) { }

    /// <inheritdoc/>
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
