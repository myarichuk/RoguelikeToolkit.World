using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 60: resolves wars into culture overlays. For every war the <see cref="WarOperator"/> logged
/// the turn before, a sufficiently stronger attacker (see
/// <see cref="HistoryDemography.ConquestAdvantage"/>) may impose its culture on the loser: the
/// loser's <see cref="Polity.CultureOverlay"/> takes the winner's culture key and a
/// <see cref="HistoryEventKind.Conquest"/> event records the transfer for the <c>Names</c> leaf,
/// which renders the losing polity through the winning table late. Conquests cost one decision
/// from this operator's own per-turn budget. No tile changes hands: conquest is a cultural overlay,
/// so the ownership projection stays valid. With <see cref="HistoryFeatures.Battles"/> on, the battle decides instead of the
/// population ratio: only a war whose field battle the attacker won decisively, or whose siege took the loser's capital, can
/// become an overlay.
/// </summary>
public sealed class ConquestOperator : IHistoryOperator
{
    /// <summary>Chance a qualifying victory becomes an overlay (the rest stay raids).</summary>
    public const double ConquestChance = 0.5;

    private PeoplesStore? _store;
    private bool _storeLoaded;

    /// <summary>Diagnostic name, <c>Conquest</c>.</summary>
    public string Name => "Conquest";

    /// <summary><see cref="StageOrder.Conquest"/> (60).</summary>
    public int Order => StageOrder.Conquest;

    /// <summary>Resolves the wars logged on the previous turn (a same-turn war is seen one turn later).</summary>
    public void Propose(TurnView view, ProposalSink sink)
    {
        var polities = view.Polities;
        if (polities.Count < 2) return;
        if (!_storeLoaded)
        {
            _store = view.LoadPeoples();
            _storeLoaded = true;
        }
        bool battles = view.Has(HistoryFeatures.Battles);
        foreach (var victor in polities)
        {
            if (!victor.Alive) continue; // a victor that collapsed or was absorbed since imposes nothing
            foreach (var war in view.EventsOf(victor.Id))
            {
                if (war.Kind != HistoryEventKind.War || war.Turn != view.Turn - 1) continue;
                int winner = victor.Id;
                if (!TryDefender(war, out int loser)) continue;
                if ((uint)loser >= (uint)polities.Count || winner == loser) continue;
                var fallen = polities[loser];
                if (!fallen.Alive || fallen.CultureOverlay == victor.CultureKey) continue;
                if (battles ? !RoutedDefender(view, war) : victor.Population < HistoryDemography.ConquestAdvantage * fallen.Population) continue;
                var stream = sink.Rng(loser, "overlay");
                if (stream.NextDouble() >= ConquestChance) continue;
                if (!sink.TrySpendDecision()) continue;
                sink.Emit(new Proposal.SetOverlay { EntityId = loser, Culture = victor.CultureKey });
                sink.Emit(new Proposal.RepayGrudge { EntityId = winner, Other = loser });
                sink.Event(HistoryEventKind.Conquest, winner, fallen.CapitalTile,
                    new[] { "conquest", $"winner:{winner}", $"loser:{loser}", victor.CultureKey }, war.Id);
            }
        }
    }

    /// <summary>
    /// True when <paramref name="war"/> narrates a field battle the attacker won decisively, or one whose siege took the
    /// defender's capital (stormed, surrendered or betrayed). A relief battle never counts.
    /// </summary>
    private static bool RoutedDefender(TurnView view, HistoryEvent war)
    {
        foreach (string tag in war.Tags)
        {
            if (!tag.StartsWith("battle:", StringComparison.Ordinal)
                || !int.TryParse(tag.Substring(7), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id)
                || (uint)id >= (uint)view.Battles.Count)
                continue;
            var battle = view.Battles[id];
            if (battle.Kind is not (BattleKind.Field or BattleKind.Ambush)) return false;
            if (battle.Outcome == BattleOutcome.AttackerDecisive) return true;
            foreach (var siege in view.Battles)
                if (siege.Parent == id && siege.Kind == BattleKind.Siege && SiegeResolver.Fell(siege.SiegeEnd)
                    && (uint)siege.Settlement < (uint)view.Settlements.Count && view.Settlements[siege.Settlement].Role == SettlementRole.Capital)
                    return true;
            return false;
        }
        return false;
    }

    private static bool TryDefender(HistoryEvent war, out int defender)
    {
        defender = -1;
        foreach (string tag in war.Tags)
        {
            if (tag.StartsWith("defender:", StringComparison.Ordinal)
                && int.TryParse(tag.Substring(9), out int id))
            {
                defender = id;
                return true;
            }
        }
        return false;
    }
}
