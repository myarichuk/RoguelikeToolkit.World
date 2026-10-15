using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 91 (post-pass): projects polity capitals onto the map as settlements and historic sites (one of each
/// per polity, founded at turn 0). Habitability snapshots the capacity layer when registered and
/// 0.5 otherwise; the settlement adapter (P4a) and the editor history panel read these lists.
/// Re-running is a no-op once projected; map edits re-resolve through
/// <see cref="HistoryCatalog.Respatialize"/> instead.
/// </summary>
public sealed class CityHistoryProjectPostPass : IHistoryPostPass
{
    /// <summary>Fallback habitability when no capacity layer is registered.</summary>
    public const float DefaultHabitability = 0.5f;

    /// <summary>Diagnostic name, <c>CityHistoryProject</c>.</summary>
    public string Name => "CityHistoryProject";

    /// <summary>Projects one settlement and one site per polity capital; a no-op once projected.</summary>
    public void Run(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        if (history.Settlements.Count > 0) return;
        var store = map.DataStore;
        bool hasCapacity = store.IsLayerRegistered<LandCapacityInfo>();
        var capacity = hasCapacity ? store.GetSpan<LandCapacityInfo>() : default;
        foreach (var polity in history.Polities)
        {
            float habitability = DefaultHabitability;
            if (hasCapacity)
            {
                int total = capacity[polity.CapitalTile].Arable + capacity[polity.CapitalTile].Pasture
                    + capacity[polity.CapitalTile].Game + capacity[polity.CapitalTile].Timber;
                habitability = Math.Clamp(total / 1020f, 0f, 1f);
            }
            var position = store.GetGeoCoord(polity.CapitalTile);
            history.Settlements.Add(new HistorySettlement
            {
                TileIndex = polity.CapitalTile,
                PolityId = polity.Id,
                FoundedTurn = 0,
                Position = position,
                Habitability = habitability,
                Active = true,
            });
            history.Sites.Add(new HistorySite
            {
                TileIndex = polity.CapitalTile,
                PolityId = polity.Id,
                FoundedTurn = 0,
                Position = position,
                Active = true,
            });
        }
    }
}
