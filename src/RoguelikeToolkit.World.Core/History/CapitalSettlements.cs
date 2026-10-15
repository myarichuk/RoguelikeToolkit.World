using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>Creates the capital <see cref="HistorySettlement"/> of a polity at the moment the polity is founded.</summary>
internal static class CapitalSettlements
{
    /// <summary>Fallback habitability when no capacity layer is registered.</summary>
    public const float DefaultHabitability = 0.5f;

    /// <summary>Capacity-derived habitability 0..1 of <paramref name="tile"/>, or the fallback.</summary>
    public static float HabitabilityOf(WorldDataStore store, int tile)
    {
        if (!store.IsLayerRegistered<LandCapacityInfo>()) return DefaultHabitability;
        var capacity = store.GetSpan<LandCapacityInfo>();
        int total = capacity[tile].Arable + capacity[tile].Pasture + capacity[tile].Game + capacity[tile].Timber;
        return Math.Clamp(total / 1020f, 0f, 1f);
    }

    /// <summary>Adds the standing capital settlement of <paramref name="polity"/>, founded on <paramref name="turn"/>.</summary>
    public static void Add(WorldDataStore store, HistoryCatalog history, Polity polity, int turn)
    {
        history.Settlements.Add(new HistorySettlement
        {
            TileIndex = polity.CapitalTile,
            PolityId = polity.Id,
            FoundedTurn = turn,
            Position = store.GetGeoCoord(polity.CapitalTile),
            Habitability = HabitabilityOf(store, polity.CapitalTile),
            Active = true,
        });
    }
}
