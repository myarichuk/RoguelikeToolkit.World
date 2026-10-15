using RoguelikeToolkit.World.Core.Climate;
using RoguelikeToolkit.World.Core.Elevation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Tectonics;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Per-tile hazard in [0, 1] for the physics-driven shock kinds, a pure function of the terrain layers (the seed is
/// folded in only to pick volcanic hot spots). Layers that are not registered contribute nothing, so a world without
/// tectonics has no quakes. <see cref="HazardOperator"/> draws against these maps each turn.
/// </summary>
public sealed class HazardMaps
{
    /// <summary>Kind key of earthquakes: plate boundaries on land, strongest on convergent and transform faults.</summary>
    public const string Earthquake = "earthquake";
    /// <summary>Kind key of eruptions: convergent arcs and rifts, thinned to a few hot spots.</summary>
    public const string Volcano = "volcano";
    /// <summary>Kind key of tsunamis: coastal land beside seismic sea floor.</summary>
    public const string Tsunami = "tsunami";
    /// <summary>Kind key of floods: river channels, weighted by discharge and low ground.</summary>
    public const string Flood = "flood";
    /// <summary>Kind key of droughts: dry, rain-seasonal land.</summary>
    public const string Drought = "drought";

    /// <summary>Share of tiles on a volcanic boundary that actually host a vent.</summary>
    public const double VentShare = 0.35;

    private readonly Dictionary<string, float[]> _maps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _totals = new(StringComparer.Ordinal);

    private HazardMaps() { }

    /// <summary>The hazard kinds this map set carries, in a fixed order.</summary>
    public static IReadOnlyList<string> Kinds { get; } = new[] { Earthquake, Volcano, Tsunami, Flood, Drought };

    /// <summary>Hazard of <paramref name="kind"/> at <paramref name="tile"/>; 0 for an unknown kind or tile.</summary>
    public double Of(string kind, int tile) =>
        _maps.TryGetValue(kind, out var map) && (uint)tile < (uint)map.Length ? map[tile] : 0;

    /// <summary>Sum of the hazard of <paramref name="kind"/> over every tile; 0 when the kind has no hazardous tile.</summary>
    public double Total(string kind) => _totals.TryGetValue(kind, out double total) ? total : 0;

    /// <summary>Builds the maps for <paramref name="map"/>'s terrain.</summary>
    public static HazardMaps Build(WorldMap map, int seed)
    {
        ArgumentNullException.ThrowIfNull(map);
        var store = map.DataStore;
        int n = store.TileCount;
        var result = new HazardMaps();
        bool hasElev = store.IsLayerRegistered<ElevationInfo>();
        var elev = hasElev ? store.GetSpan<ElevationInfo>() : default;
        var land = new bool[n];
        for (int t = 0; t < n; t++) land[t] = !hasElev || elev[t].Height >= HistoryTerritory.LandThreshold;

        var seismic = new float[n];
        var quake = new float[n];
        var volcano = new float[n];
        if (store.IsLayerRegistered<TectonicPlate>())
        {
            var plates = store.GetSpan<TectonicPlate>();
            for (int t = 0; t < n; t++)
            {
                var p = plates[t];
                if (p.BoundaryRings > 5) continue;
                double near = 1 - p.BoundaryRings / 6.0;
                double weight = p.NearestBoundary switch
                {
                    PlateBoundaryType.Convergent => 1.0,
                    PlateBoundaryType.Transform => 0.8,
                    PlateBoundaryType.Divergent => 0.4,
                    _ => 0,
                };
                seismic[t] = (float)(weight * near);
                if (land[t]) quake[t] = seismic[t];
                bool arc = p.NearestBoundary == PlateBoundaryType.Convergent && p.BoundaryRings <= 3;
                bool rift = p.NearestBoundary == PlateBoundaryType.Divergent && p.BoundaryRings <= 1;
                if ((arc || rift) && land[t] && Hot(seed, t))
                    volcano[t] = (float)((arc ? 0.9 : 0.5) * (1 - p.BoundaryRings / 4.0));
            }
        }
        result.Add(Earthquake, quake);
        result.Add(Volcano, volcano);

        var tsunami = new float[n];
        if (hasElev)
        {
            Span<int> adj = stackalloc int[6];
            for (int t = 0; t < n; t++)
            {
                if (!land[t]) continue;
                int count = store.GetAdjacent(t, adj);
                float best = 0;
                for (int k = 0; k < count; k++)
                    if (!land[adj[k]]) best = Math.Max(best, seismic[adj[k]]);
                if (best > 0) tsunami[t] = Math.Max(best, seismic[t] * 0.6f);
            }
        }
        result.Add(Tsunami, tsunami);

        var flood = new float[n];
        if (store.IsLayerRegistered<HydrologyInfo>())
        {
            var hydro = store.GetSpan<HydrologyInfo>();
            float maxFlow = 0;
            for (int t = 0; t < n; t++)
                if (hydro[t].IsRiver != 0 && land[t]) maxFlow = Math.Max(maxFlow, hydro[t].Flow);
            if (maxFlow > 0)
                for (int t = 0; t < n; t++)
                {
                    if (hydro[t].IsRiver == 0 || !land[t]) continue;
                    double size = Math.Sqrt(hydro[t].Flow / maxFlow);
                    double low = hasElev ? Math.Clamp(1.2 - elev[t].Height * 3.0, 0.3, 1.2) : 1.0;
                    flood[t] = (float)Math.Clamp((0.2 + 0.8 * size) * low, 0, 1);
                }
        }
        result.Add(Flood, flood);

        var drought = new float[n];
        if (store.IsLayerRegistered<ClimateInfo>())
        {
            var climate = store.GetSpan<ClimateInfo>();
            for (int t = 0; t < n; t++)
            {
                if (!land[t]) continue;
                double dry = Math.Clamp((0.5 - climate[t].Precipitation) / 0.5, 0, 1);
                drought[t] = (float)(dry * (0.4 + 0.6 * Math.Clamp(climate[t].RainSeasonality, 0, 1)));
            }
        }
        result.Add(Drought, drought);
        return result;
    }

    private void Add(string kind, float[] map)
    {
        double total = 0;
        foreach (float v in map) total += v;
        _maps[kind] = map;
        _totals[kind] = total;
    }

    private static bool Hot(int seed, int tile)
    {
        unchecked
        {
            ulong x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)tile * 0xBF58476D1CE4E5B9UL;
            x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27; x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (x >> 11) * (1.0 / 9007199254740992.0) < VentShare;
        }
    }
}
