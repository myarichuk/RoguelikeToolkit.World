using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Hydrology;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Settlement;
using RoguelikeToolkit.World.Core.Storage;
using RoguelikeToolkit.World.Core.Features;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 34: colonization. A polity whose crowding pressure is at or above <see cref="PressureThreshold"/> and that is
/// under its settlement cap founds one <see cref="SettlementRole.Colony"/> hamlet per turn on a tile it owns,
/// chosen among the best-scoring <see cref="CitySiteScorer"/> candidates that keep
/// <see cref="MinSpacingDegrees"/> from every standing settlement, and sheds <see cref="PressureRelief"/> of
/// pressure for it. Each founding spends one operator emergence. The candidate list is a pure function of the
/// map, computed on turn 1 (or the first turn run) and reused.
/// </summary>
public sealed class SettleOperator : IHistoryOperator
{
    /// <summary>Crowding pressure at which a polity looks for new land.</summary>
    public const double PressureThreshold = 1.0;
    /// <summary>Pressure shed by one founding.</summary>
    public const double PressureRelief = 0.4;
    /// <summary>Smallest angular gap, in degrees, between a new settlement and any standing one.</summary>
    public const double MinSpacingDegrees = 4.0;
    /// <summary>How many of the best eligible candidates the pick chooses among.</summary>
    public const int PickAmong = 3;
    /// <summary>A pool group at least this fraction of the polity's mean settlement population founds a refuge; smaller ones merge into a standing settlement.</summary>
    public const double RefugeFoundShare = 0.5;
    /// <summary>How many spaced candidates are weighed by history before the best <see cref="PickAmong"/> are kept.</summary>
    public const int ScanLimit = 24;
    /// <summary>Weight of a candidate on blasted ground (an active crater, plague pit, scorched field or fissure within <see cref="ScarRadiusDegrees"/>).</summary>
    public const double ScarredWeight = 0.25;
    /// <summary>Weight of a candidate on a ruin: people return to old sites.</summary>
    public const double RuinWeight = 1.5;
    /// <summary>Weight of a candidate near a live trade route (within <see cref="RouteRadiusDegrees"/> of its end).</summary>
    public const double RouteWeight = 1.3;
    /// <summary>Angular reach, in degrees, of a non-ruin scar.</summary>
    public const double ScarRadiusDegrees = 3.0;
    /// <summary>Angular reach, in degrees, of a route end.</summary>
    public const double RouteRadiusDegrees = 6.0;
    private const int CandidateLimit = 4000;

    private List<CitySiteScore>? _candidates;

    /// <summary>Diagnostic name, <c>Settle</c>.</summary>
    public string Name => "Settle";

    /// <summary><see cref="StageOrder.Settle"/> (34).</summary>
    public int Order => StageOrder.Settle;

    /// <summary>Proposes at most one colony per pressured polity.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        if (view.Polities.Count == 0) return;
        if (view.Turn == 1 || _candidates == null) _candidates = RankCandidates(view);
        if (_candidates.Count == 0) return;
        var vectors = view.Map.DataStore.GetTileVectors();
        double minDot = DetMath.Cos(MinSpacingDegrees * GeoCoord.Deg2Rad);

        foreach (var polity in view.Polities)
        {
            if (!polity.Alive) continue;
            int standing = 0;
            double standingPeople = 0;
            foreach (var s in view.Settlements)
                if (!s.Ended && s.PolityId == polity.Id) { standing++; standingPeople += s.Population; }
            if (ProposeRefuge(view, sink, polity.Id, standing, standingPeople, vectors, minDot)) continue;
            if (view.ScalarOf(polity.Id, ScalarRegistry.Pressure) < PressureThreshold) continue;
            if (standing >= view.MaxSettlementsPerPolity) continue;

            var eligible = new List<(CitySiteScore Site, int Parent, double Dot)>();
            foreach (var site in _candidates)
            {
                if (view.OwnerOf(site.TileIndex) != polity.Id) continue;
                bool spaced = true;
                int parent = -1;
                double nearest = double.NegativeInfinity;
                foreach (var s in view.Settlements)
                {
                    if (s.Ended) continue;
                    double dot = Vector3D.Dot(vectors[site.TileIndex], vectors[s.Tile]);
                    if (dot >= minDot) { spaced = false; break; }
                    if (s.PolityId == polity.Id && dot > nearest) { nearest = dot; parent = s.Index; }
                }
                if (!spaced || parent < 0) continue;
                eligible.Add((site, parent, nearest));
                if (eligible.Count == ScanLimit) break;
            }
            if (eligible.Count == 0) continue;
            if (!sink.TrySpendEmergence()) return;
            var rng = HistoryRng.Create(view.Seed, nameof(SettleOperator), view.Turn, polity.Id, "site");
            var best = BestByHistory(view, eligible, e => e.Site, vectors);
            var chosen = best[(int)rng.NextUInt((uint)best.Count)];
            sink.Emit(new Proposal.AddSettlement
            {
                EntityId = polity.Id,
                Tile = chosen.Site.TileIndex,
                Role = SettlementRole.Colony,
                Tier = SettlementTier.Hamlet,
                Parent = chosen.Parent,
            });
            sink.Adjust(polity.Id, ScalarRegistry.Pressure, -PressureRelief);
        }
    }

    /// <summary>
    /// Settles the polity's displaced groups: a group of at least <see cref="RefugeFoundShare"/> of the mean settlement
    /// population founds a <see cref="SettlementRole.Refuge"/> (one per turn, defensible or coastal sites first) when the
    /// polity is under its cap; every other group merges into the nearest standing settlement below <see cref="PressureThreshold"/> (else the nearest at all). Returns true
    /// when a refuge was proposed this turn.
    /// </summary>
    private bool ProposeRefuge(TurnView view, ProposalSink sink, int polityId, int standing, double standingPeople,
        ReadOnlySpan<Vector3D> vectors, double minDot)
    {
        bool founded = false;
        double mean = standing > 0 ? standingPeople / standing : 0;
        foreach (var group in view.DisplacedPool)
        {
            if (group.PolityId != polityId || !(group.Amount > 0)) continue;
            if (!founded && standing < view.MaxSettlementsPerPolity && group.Amount >= RefugeFoundShare * mean
                && TryFoundRefuge(view, sink, polityId, group, vectors, minDot)) { founded = true; continue; }
            int origin = group.Origin;
            if (standing == 0)
            {
                ProposeAsylum(view, sink, group, vectors);
                continue;
            }
            int best = -1, bestRoomy = -1;
            double nearest = double.NegativeInfinity, nearestRoomy = double.NegativeInfinity;
            foreach (var s in view.Settlements)
            {
                if (s.Ended || s.PolityId != polityId) continue;
                double dot = (uint)origin < (uint)view.Settlements.Count
                    ? Vector3D.Dot(vectors[s.Tile], vectors[view.Settlements[origin].Tile]) : 0;
                if (dot > nearest) { nearest = dot; best = s.Index; }
                if (dot > nearestRoomy && view.SettlementPressure(s.Index) < PressureThreshold) { nearestRoomy = dot; bestRoomy = s.Index; }
            }
            // Refugees prefer the nearest settlement that still has room; an already crowded one takes them only as a last resort.
            if (bestRoomy >= 0) best = bestRoomy;
            if (best >= 0)
                sink.Emit(new Proposal.AbsorbDisplaced { EntityId = polityId, Group = group.Index, Settlement = best, Amount = group.Amount });
        }
        return founded;
    }

    /// <summary>
    /// A polity with no standing settlement cannot take its people back: the group asks the nearest standing
    /// settlement of any living polity, preferring one with room (cross-polity asylum). With nobody standing it decays to the lost.
    /// </summary>
    private static void ProposeAsylum(TurnView view, ProposalSink sink, TurnView.DisplacedState group, ReadOnlySpan<Vector3D> vectors)
    {
        if ((uint)group.Origin >= (uint)view.Settlements.Count) return;
        var from = vectors[view.Settlements[group.Origin].Tile];
        int best = -1, bestRoomy = -1;
        double nearest = double.NegativeInfinity, nearestRoomy = double.NegativeInfinity;
        foreach (var s in view.Settlements)
        {
            if (s.Ended || s.PolityId == group.PolityId || !view.Polities[s.PolityId].Alive) continue;
            double dot = Vector3D.Dot(vectors[s.Tile], from);
            if (dot > nearest) { nearest = dot; best = s.Index; }
            if (dot > nearestRoomy && view.SettlementPressure(s.Index) < PressureThreshold) { nearestRoomy = dot; bestRoomy = s.Index; }
        }
        if (bestRoomy >= 0) best = bestRoomy;
        if (best >= 0)
            sink.Emit(new Proposal.AbsorbDisplaced { EntityId = group.PolityId, Group = group.Index, Settlement = best, Amount = group.Amount });
    }

    private bool TryFoundRefuge(TurnView view, ProposalSink sink, int polityId, TurnView.DisplacedState group,
        ReadOnlySpan<Vector3D> vectors, double minDot)
    {
        if (_candidates == null || _candidates.Count == 0) return false;
        var preferred = new List<(CitySiteScore Site, int Parent)>();
        var fallback = new List<(CitySiteScore Site, int Parent)>();
        foreach (var site in _candidates)
        {
            if (preferred.Count == ScanLimit) break;
            if (view.OwnerOf(site.TileIndex) != polityId) continue;
            bool spaced = true;
            int parent = -1;
            double nearest = double.NegativeInfinity;
            foreach (var s in view.Settlements)
            {
                if (s.Ended) continue;
                double dot = Vector3D.Dot(vectors[site.TileIndex], vectors[s.Tile]);
                if (dot >= minDot) { spaced = false; break; }
                if (s.PolityId == polityId && dot > nearest) { nearest = dot; parent = s.Index; }
            }
            if (!spaced) continue;
            if ((uint)group.Origin < (uint)view.Settlements.Count && !view.Settlements[group.Origin].Ended
                && view.Settlements[group.Origin].PolityId == polityId) parent = group.Origin;
            bool safe = site.Reasons.Contains("high", StringComparison.Ordinal) || site.Reasons.Contains("water", StringComparison.Ordinal);
            if (safe) preferred.Add((site, parent));
            else if (fallback.Count < ScanLimit) fallback.Add((site, parent));
        }
        var pool = preferred.Count > 0 ? preferred : fallback;
        if (pool.Count == 0 || !sink.TrySpendEmergence()) return false;
        var rng = HistoryRng.Create(view.Seed, nameof(SettleOperator), view.Turn, polityId, "refuge");
        var best = BestByHistory(view, pool, e => e.Site, vectors);
        var chosen = best[(int)rng.NextUInt((uint)best.Count)];
        sink.Emit(new Proposal.AddSettlement
        {
            EntityId = polityId,
            Tile = chosen.Site.TileIndex,
            Role = SettlementRole.Refuge,
            Tier = SettlementTier.Hamlet,
            Parent = chosen.Parent,
            Cause = group.Cause,
            FromGroup = group.Index,
        });
        return true;
    }

    /// <summary>
    /// Ranks spaced candidates by scorer score times a history weight (scarred ground avoided, ruins reoccupied, live routes
    /// favoured) and returns the best <see cref="PickAmong"/>; ties keep scorer order, so the result is deterministic.
    /// </summary>
    internal static List<T> BestByHistory<T>(TurnView view, List<T> items, Func<T, CitySiteScore> site, ReadOnlySpan<Vector3D> vectors)
    {
        double scarDot = DetMath.Cos(ScarRadiusDegrees * GeoCoord.Deg2Rad);
        double routeDot = DetMath.Cos(RouteRadiusDegrees * GeoCoord.Deg2Rad);
        var ranked = new List<(T Item, double Weight, int Order)>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var c = site(items[i]);
            var v = vectors[c.TileIndex];
            double w = c.Score;
            bool blasted = false, ruin = false;
            foreach (var sc in view.Scars)
            {
                if (!sc.Active || sc.Kind == HistoryScar.Battlefield) continue;
                if (sc.Kind == HistoryScar.Ruin) { if (sc.Tile == c.TileIndex) ruin = true; }
                else if (Vector3D.Dot(v, vectors[sc.Tile]) >= scarDot) blasted = true;
            }
            if (blasted) w *= ScarredWeight;
            else if (ruin) w *= RuinWeight;
            foreach (var r in view.Roads)
            {
                if (r.Severed) continue;
                if (Vector3D.Dot(v, vectors[r.TileA]) >= routeDot || Vector3D.Dot(v, vectors[r.TileB]) >= routeDot) { w *= RouteWeight; break; }
            }
            ranked.Add((items[i], w, i));
        }
        ranked.Sort((a, b) => a.Weight != b.Weight ? b.Weight.CompareTo(a.Weight) : a.Order.CompareTo(b.Order));
        var result = new List<T>(PickAmong);
        for (int i = 0; i < ranked.Count && i < PickAmong; i++) result.Add(ranked[i].Item);
        return result;
    }

    private static List<CitySiteScore> RankCandidates(TurnView view)
    {
        var rivers = new RiverCatalog();
        var waters = new WaterBodyCatalog();
        HydrologyStage.PopulateCatalogs(view.Map, rivers, waters);
        return CitySiteScorer.Score(view.Map.DataStore, rivers, waters,
            new CitySiteFilter { TopN = CandidateLimit, FreshWaterRadiusKm = 400 });
    }
}
