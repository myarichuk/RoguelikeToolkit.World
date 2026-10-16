using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// The founding sources besides pressure colonization and refuges, run by <see cref="SettleOperator"/> from its shared
/// candidate list: a garrison <see cref="SettlementRole.Post"/> facing a grudge rival, a pilgrimage
/// <see cref="SettlementRole.Shrine"/> beside an own holy site, a route <see cref="SettlementRole.Node"/> on water near
/// an own road end, and the sedentarization of a <see cref="PolityForm.Horde"/> that still holds only its seat.
/// Each polity founds at most one of these per turn, rolled with a pure draw of <c>(seed, turn, polity, source)</c>.
/// </summary>
internal static class FoundingSources
{
    /// <summary>Per-turn chance a polity facing a grudge rival garrisons its border.</summary>
    public const double PostChance = 0.04;
    /// <summary>Per-turn chance an unserved holy site draws a pilgrimage town.</summary>
    public const double ShrineChance = 0.15;
    /// <summary>Per-turn chance a polity with a road end on water founds a route node.</summary>
    public const double NodeChance = 0.06;
    /// <summary>Per-turn chance a seat-only horde settles down once old enough.</summary>
    public const double SedentarizeChance = 0.1;
    /// <summary>Turns a horde roams before it may settle.</summary>
    public const int SedentarizeAge = 20;
    /// <summary>Most posts and nodes one polity holds.</summary>
    public const int RoleCap = 2;
    /// <summary>A <see cref="PolityForm.MerchantRepublic"/> founds route nodes this many times as often and holds one more.</summary>
    public const double RepublicNodeFactor = 2.0;
    /// <summary>A post stands within this angular distance of a rival settlement.</summary>
    public const double PostRangeDegrees = 8.0;
    /// <summary>A node stands within this angular distance of an own road end.</summary>
    public const double NodeRangeDegrees = 3.0;

    /// <summary>Proposes at most one special founding for <paramref name="polity"/>; true when it did.</summary>
    internal static bool Propose(TurnView view, ProposalSink sink, TurnView.PolityState polity, List<CitySiteScore> candidates,
        ReadOnlySpan<Vector3D> vectors, double minDot)
    {
        int standing = 0, posts = 0, nodes = 0;
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId != polity.Id) continue;
            standing++;
            if (s.Role == SettlementRole.Post) posts++;
            else if (s.Role == SettlementRole.Node) nodes++;
        }
        if (standing == 0 || standing >= view.MaxSettlementsPerPolity) return false;

        if (posts < RoleCap && Roll(view, polity, "post") < PostChance)
        {
            double range = DetMath.Cos(PostRangeDegrees * GeoCoord.Deg2Rad);
            if (TryFound(view, sink, polity, candidates, vectors, minDot, SettlementRole.Post, "garrison", "post",
                (site, v, vs) => FacesRival(view, polity.Id, v, vs, range), preferHigh: true)) return true;
        }

        double holyDot = DetMath.Cos(SettleOperator.HolySiteRadiusDegrees * GeoCoord.Deg2Rad);
        foreach (var h in view.HolySites)
        {
            if (!h.Active || h.PolityId != polity.Id || Served(view, h.Tile, vectors, holyDot)) continue;
            if (Roll(view, polity, "shrine" + h.Tile) >= ShrineChance) continue;
            var hv = vectors[h.Tile];
            if (TryFound(view, sink, polity, candidates, vectors, minDot, SettlementRole.Shrine, "pilgrimage", "shrine" + h.Tile,
                (site, v, vs) => Vector3D.Dot(v, hv) >= holyDot, preferHigh: false)) return true;
        }

        bool republic = polity.Form == PolityForm.MerchantRepublic;
        if (nodes < RoleCap + (republic ? 1 : 0) && Roll(view, polity, "node") < NodeChance * (republic ? RepublicNodeFactor : 1.0))
        {
            double near = DetMath.Cos(NodeRangeDegrees * GeoCoord.Deg2Rad);
            if (TryFound(view, sink, polity, candidates, vectors, minDot, SettlementRole.Node, "route", "node",
                (site, v, vs) => OnWater(site) && NearRoadEnd(view, polity.Id, v, vs, near), preferHigh: false)) return true;
        }

        if (polity.Form == PolityForm.Horde && standing == 1 && view.Turn - polity.FoundedTurn >= SedentarizeAge
            && Roll(view, polity, "settle") < SedentarizeChance)
        {
            if (TryFound(view, sink, polity, candidates, vectors, minDot, SettlementRole.Colony, "sedentarized", "settle",
                (site, v, vs) => true, preferHigh: false, preferWater: true)) return true;
        }
        return false;
    }

    private delegate bool Accept(CitySiteScore site, Vector3D v, ReadOnlySpan<Vector3D> vectors);

    private static double Roll(TurnView view, TurnView.PolityState polity, string purpose)
        => HistoryRng.Create(view.Seed, nameof(FoundingSources), view.Turn, polity.Id, purpose).NextDouble();

    private static bool OnWater(CitySiteScore site)
        => site.Reasons.Contains("river", StringComparison.Ordinal) || site.Reasons.Contains("water", StringComparison.Ordinal);

    private static bool FacesRival(TurnView view, int polityId, Vector3D v, ReadOnlySpan<Vector3D> vectors, double range)
    {
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId == polityId || !view.Polities[s.PolityId].Alive) continue;
            if (view.GrudgeWeight(polityId, s.PolityId) <= 0 || view.IsWindowed(polityId, s.PolityId)) continue;
            if (Vector3D.Dot(v, vectors[s.Tile]) >= range) return true;
        }
        return false;
    }

    private static bool NearRoadEnd(TurnView view, int polityId, Vector3D v, ReadOnlySpan<Vector3D> vectors, double near)
    {
        foreach (var r in view.Roads)
        {
            if (r.Severed || r.PolityId != polityId) continue;
            if (Vector3D.Dot(v, vectors[r.TileA]) >= near || Vector3D.Dot(v, vectors[r.TileB]) >= near) return true;
        }
        return false;
    }

    private static bool Served(TurnView view, int holyTile, ReadOnlySpan<Vector3D> vectors, double holyDot)
    {
        foreach (var s in view.Settlements)
            if (!s.Ended && s.Role == SettlementRole.Shrine && Vector3D.Dot(vectors[s.Tile], vectors[holyTile]) >= holyDot) return true;
        return false;
    }

    private static bool TryFound(TurnView view, ProposalSink sink, TurnView.PolityState polity, List<CitySiteScore> candidates,
        ReadOnlySpan<Vector3D> vectors, double minDot, SettlementRole role, string why, string purpose,
        Accept accept, bool preferHigh, bool preferWater = false)
    {
        var eligible = new List<(CitySiteScore Site, int Parent)>();
        var preferred = new List<(CitySiteScore Site, int Parent)>();
        foreach (var site in candidates)
        {
            if (preferred.Count == SettleOperator.ScanLimit) break;
            if (view.OwnerOf(site.TileIndex) != polity.Id) continue;
            var v = vectors[site.TileIndex];
            bool spaced = true;
            int parent = -1;
            double nearest = double.NegativeInfinity;
            foreach (var s in view.Settlements)
            {
                if (s.Ended) continue;
                double dot = Vector3D.Dot(v, vectors[s.Tile]);
                if (dot >= minDot) { spaced = false; break; }
                if (s.PolityId == polity.Id && dot > nearest) { nearest = dot; parent = s.Index; }
            }
            if (!spaced || parent < 0 || !accept(site, v, vectors)) continue;
            bool good = (preferHigh && site.Reasons.Contains("high", StringComparison.Ordinal)) || (preferWater && OnWater(site));
            if (good) preferred.Add((site, parent));
            else if (eligible.Count < SettleOperator.ScanLimit) eligible.Add((site, parent));
        }
        var pool = preferred.Count > 0 ? preferred : eligible;
        if (pool.Count == 0 || !sink.TrySpendEmergence()) return false;
        var best = SettleOperator.BestByHistory(view, pool, e => e.Site, vectors);
        var rng = HistoryRng.Create(view.Seed, nameof(FoundingSources), view.Turn, polity.Id, purpose + ":site");
        var chosen = best[(int)rng.NextUInt((uint)best.Count)];
        sink.Emit(new Proposal.AddSettlement
        {
            EntityId = polity.Id,
            Tile = chosen.Site.TileIndex,
            Role = role,
            Tier = SettlementTier.Hamlet,
            Parent = chosen.Parent,
            Why = why,
        });
        return true;
    }
}
