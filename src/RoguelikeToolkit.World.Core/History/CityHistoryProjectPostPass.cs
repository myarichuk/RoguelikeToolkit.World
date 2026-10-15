using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 91 (post-pass): projects polity capitals onto the map as historic sites (one per polity, founded at turn 0). Habitability snapshots the capacity layer when registered and
/// 0.5 otherwise; the settlement adapter (P4a) and the editor history panel read these lists.
/// Re-running is a no-op once projected; map edits re-resolve through
/// <see cref="HistoryCatalog.Respatialize"/> instead.
/// </summary>
public sealed class CityHistoryProjectPostPass : IHistoryPostPass
{
    /// <summary>Fallback habitability when no capacity layer is registered.</summary>
    public const float DefaultHabitability = CapitalSettlements.DefaultHabitability;

    /// <summary>Diagnostic name, <c>CityHistoryProject</c>.</summary>
    public string Name => "CityHistoryProject";

    /// <summary>
    /// Projects one historic site per polity capital, once. Capital settlements already exist: the formation
    /// step and the polity-founding commit create them as the polities appear.
    /// </summary>
    public void Run(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        var store = map.DataStore;
        foreach (var polity in history.Polities)
        {
            if (history.Sites.Any(s => s.TileIndex == polity.CapitalTile && s.PolityId == polity.Id && s.FoundedTurn == 0)) continue;
            history.Sites.Add(new HistorySite
            {
                TileIndex = polity.CapitalTile,
                PolityId = polity.Id,
                FoundedTurn = 0,
                Position = store.GetGeoCoord(polity.CapitalTile),
                Active = true,
            });
        }
    }
}
