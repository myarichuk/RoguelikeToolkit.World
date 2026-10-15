using RoguelikeToolkit.World.Core.Generation;
using RoguelikeToolkit.World.Core.Peoples;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Order 84 (post-pass): faith. Runs once after the last turn, so each polity's faith reads its whole
/// lived history: the culture's faith form, two to seven power domains drawn from the pressures
/// the polity lived under (wars, road cuts, desecrations, treaties, tribute, villains), and myth
/// slots (origin plus one trauma slot per remembered disaster). Data and tags only: text renders
/// late through <see cref="Integration.IMythTextProvider"/>, so stubbing the provider never
/// changes the event log. One <see cref="HistoryEventKind.Myth"/> event records each faith.
/// </summary>
public sealed class FaithPostPass : IHistoryPostPass
{
    /// <summary>Fewest powers a faith carries.</summary>
    public const int MinPowers = 2;

    /// <summary>Most powers a faith carries.</summary>
    public const int MaxPowers = 7;

    /// <summary>Most trauma myth slots a faith keeps.</summary>
    public const int MaxTraumaSlots = 3;

    /// <summary>Diagnostic name, <c>Faith</c>.</summary>
    public string Name => "Faith";

    /// <summary>Builds one faith per polity in polity-id order; a no-op once faiths exist.</summary>
    public void Run(WorldMap map, HistoryCatalog history, HistoryContext context)
    {
        if (history.Faiths.Count > 0) return;
        PeoplesStore? store = PeoplesLoader.LoadStore(history);
        foreach (var polity in history.Polities)
        {
            var faith = new HistoryFaith
            {
                PolityId = polity.Id,
                Form = HolySiteOperator.FaithFormOf(polity.CultureKey, store),
            };
            DrawPowers(history, polity, faith);
            DrawMyths(history, polity, faith);
            history.Faiths.Add(faith);
            var tags = new List<string> { "myth", $"faith:{faith.Form}" };
            foreach (string power in faith.Powers) tags.Add($"power:{power}");
            tags.AddRange(faith.MythSlots);
            history.AppendEvent(HistoryEventKind.Myth, Math.Max(context.TurnCount - 1, 0), polity.Id, polity.CapitalTile, tags);
        }
    }

    private static void DrawPowers(HistoryCatalog history, Polity polity, HistoryFaith faith)
    {
        var domains = new List<string> { "harvest", "hearth" };
        int wars = 0, cuts = 0, desecrations = 0, treaties = 0, tributes = 0;
        foreach (var e in history.EventsOf(polity.Id))
        {
            switch (e.Kind)
            {
                case HistoryEventKind.War: wars++; break;
                case HistoryEventKind.RoadCut: cuts++; break;
                case HistoryEventKind.Desecration: desecrations++; break;
                case HistoryEventKind.Treaty: treaties++; break;
                case HistoryEventKind.Tribute: tributes++; break;
            }
        }
        if (wars == 0 && history.Events.Any(e => e.Kind == HistoryEventKind.War && e.Tags.Contains($"defender:{polity.Id}"))) wars++;
        if (wars > 0) domains.Add("war");
        if (cuts > 0) domains.Add("trade");
        if (desecrations > 0) domains.Add("vengeance");
        if (treaties > 0) domains.Add("peace");
        if (tributes > 0) domains.Add("dominion");
        if (polity.Pressure >= 0.5) domains.Add("hunger");
        if (polity.HasVillain) domains.Add("shadow");
        foreach (string domain in domains)
        {
            if (faith.Powers.Count >= MaxPowers) break;
            faith.Powers.Add(domain);
        }
    }

    private static void DrawMyths(HistoryCatalog history, Polity polity, HistoryFaith faith)
    {
        faith.MythSlots.Add($"origin:{polity.FolkKey}");
        int traumas = 0;
        foreach (var e in history.EventsOf(polity.Id))
        {
            if (traumas >= MaxTraumaSlots) break;
            string? slot = e.Kind switch
            {
                HistoryEventKind.War => $"trauma:war:{e.Turn}",
                HistoryEventKind.Desecration => $"trauma:desecration:{e.Turn}",
                HistoryEventKind.Conquest => $"trauma:conquest:{e.Turn}",
                _ => null,
            };
            if (slot == null) continue;
            faith.MythSlots.Add(slot);
            traumas++;
        }
    }
}
