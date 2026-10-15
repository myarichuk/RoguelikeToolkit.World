using System.Globalization;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Turns a <see cref="Shock"/> into proposals. For each standing settlement in the footprint, an intensity
/// <c>severity · reach · defence</c> is rolled against a stream per <c>(seed, operator, turn, settlement, "shock:" + kind)</c>:
/// a roll below the intensity means the settlement is struck. A struck settlement loses
/// <see cref="Shock.Casualty"/> and releases <see cref="Shock.Displacement"/> of its people (both scaled by intensity); at
/// <see cref="AbandonIntensity"/> or more it is abandoned outright (survivors released, the dead recorded as lost).
/// One <see cref="HistoryEventKind.Shock"/> event is logged per shock that struck anyone.
/// </summary>
public static class ShockResolver
{
    /// <summary>Intensity at which a struck settlement is abandoned instead of merely hurt.</summary>
    public const double AbandonIntensity = 0.8;

    /// <summary>Resolves <paramref name="shock"/> against <paramref name="view"/>'s standing settlements; returns how many were struck.</summary>
    public static int Resolve(TurnView view, ProposalSink sink, Shock shock)
    {
        if (string.IsNullOrWhiteSpace(shock.Kind)) throw new ArgumentException("Shock kind must be non-blank.", nameof(shock));
        double severity = Math.Clamp(shock.Severity, 0, 1);
        if (!(severity > 0)) return 0;
        ReadOnlySpan<Vector3D> vectors = default;
        double cosRadius = 1;
        if (shock.Footprint == ShockFootprint.Radius)
        {
            if (!(shock.RadiusDegrees > 0) || (uint)shock.Tile >= (uint)view.Map.DataStore.GetTileVectors().Length) return 0;
            vectors = view.Map.DataStore.GetTileVectors();
            cosRadius = DetMath.Cos(shock.RadiusDegrees * GeoCoord.Deg2Rad);
        }
        int struck = 0;
        int ownerPolity = -1;
        foreach (var s in view.Settlements)
        {
            if (s.Ended) continue;
            double reach = 1;
            switch (shock.Footprint)
            {
                case ShockFootprint.Tile: if (s.Tile != shock.Tile) continue; break;
                case ShockFootprint.Polity: if (s.PolityId != shock.PolityId) continue; break;
                case ShockFootprint.Radius:
                    double dot = Vector3D.Dot(vectors[shock.Tile], vectors[s.Tile]);
                    if (dot < cosRadius) continue;
                    double angle = Math.Acos(Math.Min(1, dot));
                    reach = 1 - angle / (shock.RadiusDegrees * GeoCoord.Deg2Rad);
                    break;
            }
            double intensity = Math.Clamp(severity * reach * Defence(s), 0, 1);
            if (!(intensity > 0)) continue;
            var rng = sink.Rng(s.Index, "shock:" + shock.Kind);
            if (rng.NextDouble() >= intensity) continue;
            struck++;
            if (ownerPolity < 0) ownerPolity = s.PolityId;
            double casualty = Math.Clamp(shock.Casualty * intensity, 0, 1);
            double displaced = Math.Clamp(shock.Displacement * intensity, 0, 1 - casualty);
            if (intensity >= AbandonIntensity)
            {
                sink.Emit(new Proposal.EndSettlement { EntityId = s.PolityId, Settlement = s.Index, EndCause = shock.Kind, LostShare = casualty });
                continue;
            }
            if (displaced > 0)
                sink.Emit(new Proposal.ReleasePopulation { EntityId = s.PolityId, Settlement = s.Index, Share = displaced });
            double remaining = 1 - displaced;
            if (casualty > 0 && remaining > 0)
                sink.Emit(new Proposal.KillPopulation { EntityId = s.PolityId, Settlement = s.Index, Share = Math.Min(1, casualty / remaining) });
        }
        if (struck == 0) return 0;
        var tags = new List<string>
        {
            "shock:" + shock.Kind,
            "severity:" + severity.ToString("0.00", CultureInfo.InvariantCulture),
            "struck:" + struck.ToString(CultureInfo.InvariantCulture),
        };
        tags.AddRange(shock.Tags);
        sink.Event(HistoryEventKind.Shock, shock.PolityId >= 0 ? shock.PolityId : ownerPolity, shock.Tile, tags);
        return struck;
    }

    /// <summary>How exposed a settlement is: bigger tiers and fortified roles shrug harm off, hamlets and refuges feel all of it.</summary>
    private static double Defence(TurnView.SettlementState s)
    {
        double tier = s.Tier switch
        {
            SettlementTier.Hamlet => 1.2,
            SettlementTier.Village => 1.0,
            SettlementTier.Town => 0.85,
            _ => 0.7,
        };
        double role = s.Role switch
        {
            SettlementRole.Capital => 0.8,
            SettlementRole.Post => 0.7,
            SettlementRole.Refuge => 1.15,
            _ => 1.0,
        };
        return tier * role;
    }
}
