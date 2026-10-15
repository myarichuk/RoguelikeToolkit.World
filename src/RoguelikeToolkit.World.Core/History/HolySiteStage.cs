using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 44: raises holy sites. Every founded polity rolls once to raise a shrine on its
/// highest-surplus owned non-capital tile: the site shelters its polity (see
/// <see cref="HistoryCatalog.HolyDangerDelta"/>) and relieves a little crowding pressure (the
/// aura income, abstract). A later war striking the tile trips the desecration response (see
/// <see cref="WarStage"/>). Raising costs one emergence from this stage's own turn-0 budget.
/// Every draw is a pure function of <c>(seed, turn, polity, purpose)</c>.
/// </summary>
public sealed class HolySiteStage : HistoryPluginBase
{
    /// <summary>Chance a founded polity raises a holy site.</summary>
    public const double HolyChance = 0.5;

    /// <summary>Crowding pressure the sheltering aura relieves at raising time.</summary>
    public const double AuraRelief = 0.1;

    /// <summary>Diagnostic name, <c>HolySite</c>.</summary>
    public override string Name => "HolySite";
    /// <summary><see cref="StageOrder.HolySite"/> (44).</summary>
    public override int Order => StageOrder.HolySite;
    /// <summary>None required: ownership alone places the site.</summary>
    public override Type[] Reads => Array.Empty<Type>();
    /// <summary>Land capacity (real surplus) and biomes (fallback surplus) for the site pick.</summary>
    public override Type[] ReadsOptional => new[] { typeof(LandCapacityInfo), typeof(BiomeInfo) };

    /// <summary>True once polities have formed and no holy site has been raised yet.</summary>
    public override bool Precondition(WorldMap map, HistoryCatalog history)
        => history.PolitiesFormed && history.HolySites.Count == 0;

    /// <summary>Raises at most one holy site per founded polity.</summary>
    public override void Apply(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        context.ResetOperatorBudget(Name);
        PeoplesStore? store = PeoplesLoader.LoadStore(history);
        foreach (var polity in history.Polities)
        {
            var stream = HistoryRng.Create(history.Seed, Name, 0, polity.Id, "found");
            if (stream.NextDouble() >= HolyChance) continue;
            int tile = BestHolyTile(map, history, polity.Id);
            if (tile < 0) continue;
            if (!context.TrySpendOperatorEmergence(Name, 0)) continue;
            history.HolySites.Add(new HistoryHolySite
            {
                TileIndex = tile,
                PolityId = polity.Id,
                FoundedTurn = 0,
                Position = map.DataStore.GetGeoCoord(tile),
            });
            polity.Pressure = Math.Max(0, polity.Pressure - AuraRelief);
            history.AppendEvent(HistoryEventKind.HolyFound, 0, polity.Id, tile,
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

    private static int BestHolyTile(WorldMap map, HistoryCatalog history, int polityId)
    {
        var store = map.DataStore;
        bool hasCapacity = store.IsLayerRegistered<LandCapacityInfo>();
        var capacity = hasCapacity ? store.GetSpan<LandCapacityInfo>() : default;
        bool hasBiome = !hasCapacity && store.IsLayerRegistered<BiomeInfo>();
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;
        int capital = history.Polities[polityId].CapitalTile;
        int best = -1;
        int bestSum = 0;
        int tiles = Math.Min(store.TileCount, history.Ownership.Length);
        for (int tile = 0; tile < tiles; tile++)
        {
            if (history.Ownership[tile] != polityId || tile == capital) continue;
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
