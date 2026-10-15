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
            if (view.ScalarOf(polity.Id, ScalarRegistry.Pressure) < PressureThreshold) continue;
            int standing = 0;
            foreach (var s in view.Settlements)
                if (!s.Ended && s.PolityId == polity.Id) standing++;
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
                if (eligible.Count == PickAmong) break;
            }
            if (eligible.Count == 0) continue;
            if (!sink.TrySpendEmergence()) return;
            var rng = HistoryRng.Create(view.Seed, nameof(SettleOperator), view.Turn, polity.Id, "site");
            var chosen = eligible[(int)rng.NextUInt((uint)eligible.Count)];
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

    private static List<CitySiteScore> RankCandidates(TurnView view)
    {
        var rivers = new RiverCatalog();
        var waters = new WaterBodyCatalog();
        HydrologyStage.PopulateCatalogs(view.Map, rivers, waters);
        return CitySiteScorer.Score(view.Map.DataStore, rivers, waters,
            new CitySiteFilter { TopN = CandidateLimit, FreshWaterRadiusKm = 400 });
    }
}
