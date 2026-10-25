using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 70: splits far-flung, oppressed regions off as new polities. Every turn, in polity
/// order, a polity whose crowding pressure (plus its accumulated war bumps) passes
/// <see cref="OppressionThreshold"/> and which owns a large-enough far province may see that province
/// break away: the rebel capital is its most distant owned tile, ownership is re-flooded by the driver commit from
/// all capitals (user pins win, exactly like <see cref="HistoryTerritory"/>), and a
/// <see cref="HistoryEventKind.Rebellion"/> event records the parent. Rebellions cost one
/// emergence from this operator's own per-turn budget and fill free slots below
/// <see cref="HistoryContext.MaxPolities"/>, and once the founding cap is reached they have their own allowance of
/// <see cref="HistoryContext.MaxRebellions"/> over the whole history.
/// </summary>
public sealed class RebellionOperator : IHistoryOperator
{
    /// <summary>Minimum owned tiles before a polity can lose a province.</summary>
    public const int MinRebelTiles = 8;

    /// <summary>Minimum arc between a capital and its breakaway province, in degrees.</summary>
    public const double MinSeparationDegrees = 12.0;

    /// <summary>Crowding pressure (plus war bumps) at which an oppressed province snaps.</summary>
    public const double OppressionThreshold = 0.35;

    /// <summary>Parent-culture collectivism (0..100) from which a break is a clean split of kin (full <see cref="RebelChance"/>);
    /// looser cultures break too, at <see cref="LooseRebelShare"/> of it. Every culture can rebel: gating on collectivism
    /// alone used to let only a few peoples (lizardfolk, dogfolk, dwarves, orcs) multiply into breakaway states.</summary>
    public const int TightnessThreshold = 60;

    /// <summary>Per-province, per-turn breakaway chance once every condition holds, for a tight culture.</summary>
    public const double RebelChance = 0.3;

    /// <summary>Share of <see cref="RebelChance"/> a culture below <see cref="TightnessThreshold"/> breaks at.</summary>
    public const double LooseRebelShare = 0.7;

    private PeoplesStore? _store;
    private bool _storeLoaded;

    /// <summary>Diagnostic name, <c>Rebellion</c>.</summary>
    public string Name => "Rebellion";

    /// <summary><see cref="StageOrder.Rebellion"/> (70).</summary>
    public int Order => StageOrder.Rebellion;

    /// <summary>Proposes this turn's breakaway provinces from the snapshot.</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        var polities = view.Polities;
        if (polities.Count == 0) return;
        if (!_storeLoaded)
        {
            _store = view.LoadPeoples();
            _storeLoaded = true;
        }
        var map = view.Map;
        double separationDot = DetMath.Cos(MinSeparationDegrees * GeoCoord.Deg2Rad);
        var vectors = map.DataStore.GetTileVectors();
        int[] ownership = view.OwnershipSnapshot();
        int[] owned = new int[polities.Count];
        foreach (int owner in ownership)
            if ((uint)owner < (uint)owned.Length) owned[owner]++;
        int founded = 0;

        foreach (var polity in polities)
        {
            if (polities.Count + founded >= view.MaxPolities && view.RebelCount + founded >= view.MaxRebellions) return;
            if (!polity.Alive || polity.FoundedTurn >= view.Turn) continue;
            int parent = polity.Id;
            if (owned[parent] < MinRebelTiles) continue;
            if (view.ScalarOf(parent, ScalarRegistry.Pressure) < OppressionThreshold) continue;
            int capital = FarthestTile(vectors, ownership, polity);
            if (capital < 0) continue;
            if (Vector3D.Dot(vectors[polity.CapitalTile], vectors[capital]) > separationDot) continue;
            var stream = sink.Rng(parent, "break");
            double chance = CollectivismOf(polity.CultureKey, _store) >= TightnessThreshold ? RebelChance : RebelChance * LooseRebelShare;
            if (stream.NextDouble() >= chance) continue;
            if (!sink.TrySpendEmergence()) continue;
            int id = polities.Count + founded++;
            var rising = new Proposal.AddPolity
            {
                EntityId = id,
                Form = PolityForm.LooseAssociation,
                CapitalTile = capital,
                FolkKey = polity.FolkKey,
                CultureKey = polity.CultureKey,
                Parent = parent,
            };
            sink.Emit(rising);
            sink.EventIfApplied(rising, HistoryEventKind.Rebellion, id, capital,
                new[] { "rebellion", $"parent:{parent}", polity.FolkKey, polity.CultureKey });
        }
    }

    private static int CollectivismOf(string cultureKey, PeoplesStore? store)
    {
        if (!string.IsNullOrWhiteSpace(cultureKey) && store != null
            && store.Cultures.TryGetValue(cultureKey, out var culture))
        {
            return culture.Collectivism;
        }
        if (string.Equals(cultureKey, "dwarven_clan", StringComparison.OrdinalIgnoreCase)) return 80;
        if (string.Equals(cultureKey, "human_common", StringComparison.OrdinalIgnoreCase)) return 50;
        return 50;
    }

    private static int FarthestTile(ReadOnlySpan<Vector3D> vectors, int[] ownership, TurnView.PolityState polity)
    {
        var home = vectors[polity.CapitalTile];
        int best = -1;
        double bestDot = double.MaxValue;
        for (int tile = 0; tile < ownership.Length; tile++)
        {
            if (ownership[tile] != polity.Id || tile == polity.CapitalTile) continue;
            double dot = Vector3D.Dot(home, vectors[tile]);
            if (dot < bestDot || (dot == bestDot && tile < best))
            {
                bestDot = dot;
                best = tile;
            }
        }
        return best;
    }
}
