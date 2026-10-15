using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 36: the shock producer (physics maps plus famine and plague). Each turn, for each kind in <see cref="HazardMaps.Kinds"/> and each
/// standing settlement not already inside a shock of that kind this turn, a stream per
/// <c>(seed, "Hazard", turn, settlement, kind)</c> is drawn against <c>HazardRate · hazard(tile) / ReturnPeriod</c>, the
/// return period being the mean wait, in turns, of a settlement sitting on a hazard-1 tile. A hit spends one operator
/// emergence and hands a <see cref="Shock"/> centred on that settlement's tile to <see cref="ShockResolver"/>; severity scales
/// with the local hazard. Drawing from settlements rather than from empty map keeps shocks where history can see them, while
/// the maps (a pure function of the terrain, built on turn 1 or the first turn run) decide where they are likely.
/// </summary>
public sealed class HazardOperator : IHistoryOperator
{
    /// <summary>Kind key of famines: crowded settlements, where people outrun the catchment.</summary>
    public const string Famine = "famine";
    /// <summary>Kind key of plagues: dense, well-connected settlements.</summary>
    public const string Plague = "plague";

    private sealed record HazardDef(string Kind, double ReturnPeriod, double RadiusDegrees, double Casualty, double Displacement,
        Func<TurnView, TurnView.SettlementState, HazardMaps, double> Hazard);

    private static readonly HazardDef[] Defs =
    {
        new(HazardMaps.Earthquake, 36, 6.0, 0.25, 0.35, (_, s, maps) => maps.Of(HazardMaps.Earthquake, s.Tile)),
        new(HazardMaps.Volcano, 30, 4.0, 0.30, 0.50, (_, s, maps) => maps.Of(HazardMaps.Volcano, s.Tile)),
        new(HazardMaps.Tsunami, 40, 3.0, 0.35, 0.40, (_, s, maps) => maps.Of(HazardMaps.Tsunami, s.Tile)),
        new(HazardMaps.Flood, 20, 3.0, 0.10, 0.30, (_, s, maps) => maps.Of(HazardMaps.Flood, s.Tile)),
        new(HazardMaps.Drought, 20, 10.0, 0.10, 0.50, (_, s, maps) => maps.Of(HazardMaps.Drought, s.Tile)),
        new(Famine, 8, 2.0, 0.30, 0.40, (v, s, _) => Math.Clamp(v.SettlementPressure(s.Index) - 1.0, 0, 1)),
        new(Plague, 30, 6.0, 0.40, 0.10, (v, s, _) => PlagueHazard(v, s)),
    };

    /// <summary>Every kind this operator can produce, map-driven kinds first.</summary>
    public static IReadOnlyList<string> Kinds { get; } = new[]
    {
        HazardMaps.Earthquake, HazardMaps.Volcano, HazardMaps.Tsunami, HazardMaps.Flood, HazardMaps.Drought, Famine, Plague,
    };

    private HazardMaps? _maps;

    /// <summary>Diagnostic name, <c>Hazard</c>.</summary>
    public string Name => "Hazard";

    /// <summary><see cref="StageOrder.Hazard"/> (36).</summary>
    public int Order => StageOrder.Hazard;

    /// <summary>Proposes the shocks that struck this turn.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (!(view.HazardRate > 0) || view.Settlements.Count == 0) return;
        if (view.Turn == 1 || _maps == null) _maps = HazardMaps.Build(view.Map, view.Seed);
        var vectors = view.Map.DataStore.GetTileVectors();
        for (int i = 0; i < Defs.Length; i++)
        {
            var def = Defs[i];
            if (def.Kind is not (Famine or Plague) && !(_maps.Total(def.Kind) > 0)) continue;
            double cosRadius = DetMath.Cos(def.RadiusDegrees * GeoCoord.Deg2Rad);
            var hit = new List<int>();
            foreach (var s in view.Settlements)
            {
                if (s.Ended) continue;
                double hazard = def.Hazard(view, s, _maps);
                if (!(hazard > 0)) continue;
                var rng = sink.Rng(s.Index, "hazard:" + def.Kind);
                double roll = rng.NextDouble();
                double scale = rng.NextDouble();
                if (roll >= view.HazardRate * hazard / def.ReturnPeriod) continue;
                bool covered = false;
                foreach (int tile in hit)
                    if (Vector3D.Dot(vectors[tile], vectors[s.Tile]) >= cosRadius) { covered = true; break; }
                if (covered) continue;
                if (!sink.TrySpendEmergence()) return;
                hit.Add(s.Tile);
                ShockResolver.Resolve(view, sink, new Shock
                {
                    Kind = def.Kind,
                    Severity = 0.3 + 0.7 * hazard * scale,
                    Footprint = ShockFootprint.Radius,
                    Tile = s.Tile,
                    RadiusDegrees = def.RadiusDegrees,
                    Casualty = def.Casualty,
                    Displacement = def.Displacement,
                    Scar = ScarOf(def.Kind),
                });
            }
        }
    }

    private static string? ScarOf(string kind) => kind switch
    {
        HazardMaps.Earthquake => HistoryScar.Fissure,
        HazardMaps.Volcano => HistoryScar.Crater,
        HazardMaps.Drought => HistoryScar.ScorchedField,
        Plague => HistoryScar.PlaguePit,
        _ => null,
    };

    /// <summary>Plague odds: bigger tiers are denser, and a settlement on many live routes catches contagion faster.</summary>
    private static double PlagueHazard(TurnView view, TurnView.SettlementState s)
    {
        double density = s.Tier switch
        {
            SettlementTier.Hamlet => 0.2,
            SettlementTier.Village => 0.4,
            SettlementTier.Town => 0.7,
            _ => 1.0,
        };
        var routes = new HashSet<(int, int)>();
        foreach (var road in view.Roads)
            if (!road.Severed && (road.RouteA == s.Tile || road.RouteB == s.Tile)) routes.Add((road.RouteA, road.RouteB));
        double links = Math.Min(routes.Count, 4) / 4.0;
        return density * (0.5 + 0.5 * links);
    }
}
