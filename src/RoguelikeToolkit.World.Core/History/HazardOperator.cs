using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 36: the physics-driven shock producer. Each turn, for each kind in <see cref="HazardMaps.Kinds"/> and each
/// standing settlement not already inside a shock of that kind this turn, a stream per
/// <c>(seed, "Hazard", turn, settlement, kind)</c> is drawn against <c>HazardRate · hazard(tile) / ReturnPeriod</c>, the
/// return period being the mean wait, in turns, of a settlement sitting on a hazard-1 tile. A hit spends one operator
/// emergence and hands a <see cref="Shock"/> centred on that settlement's tile to <see cref="ShockResolver"/>; severity scales
/// with the local hazard. Drawing from settlements rather than from empty map keeps shocks where history can see them, while
/// the maps (a pure function of the terrain, built on turn 1 or the first turn run) decide where they are likely.
/// </summary>
public sealed class HazardOperator : IHistoryOperator
{
    private sealed record HazardDef(string Kind, double ReturnPeriod, double RadiusDegrees, double Casualty, double Displacement);

    private static readonly HazardDef[] Defs =
    {
        new(HazardMaps.Earthquake, 36, 6.0, 0.25, 0.35),
        new(HazardMaps.Volcano, 30, 4.0, 0.30, 0.50),
        new(HazardMaps.Tsunami, 40, 3.0, 0.35, 0.40),
        new(HazardMaps.Flood, 20, 3.0, 0.10, 0.30),
        new(HazardMaps.Drought, 20, 10.0, 0.10, 0.50),
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
            if (!(_maps.Total(def.Kind) > 0)) continue;
            double cosRadius = DetMath.Cos(def.RadiusDegrees * GeoCoord.Deg2Rad);
            var hit = new List<int>();
            foreach (var s in view.Settlements)
            {
                if (s.Ended) continue;
                double hazard = _maps.Of(def.Kind, s.Tile);
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
                });
            }
        }
    }
}
