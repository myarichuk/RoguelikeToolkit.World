using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 44: raises holy sites. On the first simulated turn every living polity rolls once to raise a shrine on its
/// highest-surplus owned non-capital tile: the site shelters its polity (see
/// <see cref="HistoryCatalog.HolyDangerDelta"/>) and relieves a little crowding pressure (the
/// aura income, abstract). A later war striking the tile trips the desecration response (see
/// <see cref="WarOperator"/>). Raising costs one emergence from this operator's turn budget.
/// Every draw is a pure function of <c>(seed, turn, polity, purpose)</c>.
/// </summary>
public sealed class HolySiteOperator : IHistoryOperator
{
    /// <summary>Chance a founded polity raises a holy site.</summary>
    public const double HolyChance = 0.5;

    /// <summary>Crowding pressure the sheltering aura relieves at raising time.</summary>
    public const double AuraRelief = 0.1;

    /// <summary>Diagnostic name, <c>HolySite</c>.</summary>
    public string Name => "HolySite";

    /// <summary><see cref="StageOrder.HolySite"/> (44).</summary>
    public int Order => StageOrder.HolySite;

    /// <summary>Raises at most one holy site per living polity, on the first simulated turn only.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (view.Turn != 1 || view.HolySites.Count > 0) return;
        var store = view.LoadPeoples();
        foreach (var polity in view.Polities)
        {
            if (!polity.Alive) continue;
            var stream = sink.Rng(polity.Id, "found");
            if (stream.NextDouble() >= HolyChance) continue;
            int tile = BestHolyTile(view, polity);
            if (tile < 0) continue;
            if (!sink.TrySpendEmergence()) continue;
            sink.Emit(new Proposal.AddHolySite
            {
                EntityId = polity.Id,
                Tile = tile,
                Position = view.Map.DataStore.GetGeoCoord(tile),
            });
            sink.Adjust(polity.Id, ScalarRegistry.Pressure, -AuraRelief);
            sink.Event(HistoryEventKind.HolyFound, polity.Id, tile,
                new[] { "holy", $"faith:{FaithFormOf(polity.CultureKey, store)}", $"tile:{tile}" });
        }
    }

    /// <summary>Faith form of a culture key: the table value, else a neutral fallback. Never throws for an unknown key.</summary>
    internal static string FaithFormOf(string cultureKey, PeoplesStore? store)
    {
        if (!string.IsNullOrWhiteSpace(cultureKey) && store != null
            && store.Cultures.TryGetValue(cultureKey, out var culture)
            && !string.IsNullOrWhiteSpace(culture.FaithForm))
        {
            return culture.FaithForm;
        }
        if (string.Equals(cultureKey, "dwarven_clan", StringComparison.OrdinalIgnoreCase)) return "ancestor";
        if (string.Equals(cultureKey, "human_common", StringComparison.OrdinalIgnoreCase)) return "polytheism";
        return "animism";
    }

    private static int BestHolyTile(TurnView view, TurnView.PolityState polity)
    {
        var store = view.Map.DataStore;
        bool hasCapacity = store.IsLayerRegistered<LandCapacityInfo>();
        var capacity = hasCapacity ? store.GetSpan<LandCapacityInfo>() : default;
        bool hasBiome = !hasCapacity && store.IsLayerRegistered<BiomeInfo>();
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;
        int best = -1;
        int bestSum = 0;
        int tiles = Math.Min(store.TileCount, view.TileCount);
        for (int tile = 0; tile < tiles; tile++)
        {
            if (view.OwnerOf(tile) != polity.Id || tile == polity.CapitalTile) continue;
            int sum;
            if (hasCapacity)
            {
                var cell = capacity[tile];
                sum = HistoryDemography.CapacitySum(cell.Arable, cell.Pasture, cell.Game, cell.Timber);
            }
            else if (hasBiome)
            {
                var profile = HistoryDemography.BiomeSurplus(biomes[tile].Biome);
                sum = HistoryDemography.CapacitySum(profile[0], profile[1], profile[2], profile[3]);
            }
            else
            {
                sum = 1;
            }
            if (sum > bestSum || (sum == bestSum && (best < 0 || tile < best)))
            {
                bestSum = sum;
                best = tile;
            }
        }
        return best;
    }
}
