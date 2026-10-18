using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.History;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;

namespace EconomyPlugin;

/// <summary>Resource totals per polity, summed over the tiles it owns: the land-use layer when registered, else the biome profile. Read-only against the map.</summary>
public static class Resources
{
    /// <summary>Number of <see cref="ResourceKind"/> values.</summary>
    public const int Kinds = 4;

    /// <summary>Capacity units that make one unit of output; keeps wealth numbers readable.</summary>
    public const double Scale = 100.0;

    /// <summary>Totals indexed <c>[polity * Kinds + (int)kind]</c> for the snapshot ownership.</summary>
    public static double[] Totals(TurnView view)
    {
        var totals = new double[view.Polities.Count * Kinds];
        Add(view.Map.DataStore, view.TileCount, view.OwnerOf, view.Polities.Count, totals);
        return totals;
    }

    /// <summary>Totals for a finished catalog (used by the post-pass, which has no view).</summary>
    public static double[] Totals(WorldDataStore store, HistoryCatalog history)
    {
        var totals = new double[history.Polities.Count * Kinds];
        Add(store, history.Ownership.Length, history.OwnerOf, history.Polities.Count, totals);
        return totals;
    }

    /// <summary>Sum of every resource of one polity.</summary>
    public static double Sum(double[] totals, int polity)
    {
        double sum = 0;
        for (int k = 0; k < Kinds; k++) sum += totals[polity * Kinds + k];
        return sum;
    }

    private static void Add(WorldDataStore store, int tiles, Func<int, int> ownerOf, int polities, double[] totals)
    {
        bool hasCapacity = store.IsLayerRegistered<LandCapacityInfo>();
        bool hasBiome = !hasCapacity && store.IsLayerRegistered<BiomeInfo>();
        if (!hasCapacity && !hasBiome) return;
        var capacity = hasCapacity ? store.GetSpan<LandCapacityInfo>() : default;
        var biomes = hasBiome ? store.GetSpan<BiomeInfo>() : default;
        tiles = Math.Min(tiles, store.TileCount);
        for (int tile = 0; tile < tiles; tile++)
        {
            int owner = ownerOf(tile);
            if ((uint)owner >= (uint)polities) continue;
            int o = owner * Kinds;
            if (hasCapacity)
            {
                var cell = capacity[tile];
                totals[o + (int)ResourceKind.Food] += cell.Arable + cell.Pasture;
                totals[o + (int)ResourceKind.Timber] += cell.Timber;
                totals[o + (int)ResourceKind.Fish] += cell.Fish;
                totals[o + (int)ResourceKind.Game] += cell.Game;
            }
            else
            {
                // Default worlds carry no land-use layer: the biome profile is [arable, pasture, game, timber], no fish.
                var p = HistoryDemography.BiomeSurplus(biomes[tile].Biome);
                totals[o + (int)ResourceKind.Food] += p[0] + p[1];
                totals[o + (int)ResourceKind.Timber] += p[3];
                totals[o + (int)ResourceKind.Game] += p[2];
            }
        }
    }
}
