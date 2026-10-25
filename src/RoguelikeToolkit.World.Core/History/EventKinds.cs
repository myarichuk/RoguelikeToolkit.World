namespace RoguelikeToolkit.World.Core.History;

/// <summary>Built-in <see cref="EventKind"/> ids, mirroring <see cref="HistoryEventKind"/>.</summary>
public static class EventKinds
{
    /// <summary>A polity was founded.</summary>
    public static readonly EventKind Founding = HistoryEventKind.Founding;
    /// <summary>A map edit forked the timeline.</summary>
    public static readonly EventKind Fork = HistoryEventKind.Fork;
    /// <summary>A projected site went inactive.</summary>
    public static readonly EventKind SiteLost = HistoryEventKind.SiteLost;
    /// <summary>One polity raided another.</summary>
    public static readonly EventKind War = HistoryEventKind.War;
    /// <summary>A war ended in a culture overlay.</summary>
    public static readonly EventKind Conquest = HistoryEventKind.Conquest;
    /// <summary>A polity split.</summary>
    public static readonly EventKind Rebellion = HistoryEventKind.Rebellion;
    /// <summary>A trade route opened.</summary>
    public static readonly EventKind RoadBuilt = HistoryEventKind.RoadBuilt;
    /// <summary>A war severed a route.</summary>
    public static readonly EventKind RoadCut = HistoryEventKind.RoadCut;
    /// <summary>Diffusion along a route.</summary>
    public static readonly EventKind Exchange = HistoryEventKind.Exchange;
    /// <summary>A marriage or foster contract.</summary>
    public static readonly EventKind Treaty = HistoryEventKind.Treaty;
    /// <summary>A refused treaty logged as casus belli.</summary>
    public static readonly EventKind Grudge = HistoryEventKind.Grudge;
    /// <summary>A holy site was raised.</summary>
    public static readonly EventKind HolyFound = HistoryEventKind.HolyFound;
    /// <summary>A war struck a holy tile.</summary>
    public static readonly EventKind Desecration = HistoryEventKind.Desecration;
    /// <summary>A villain rose to rule.</summary>
    public static readonly EventKind Villain = HistoryEventKind.Villain;
    /// <summary>A steppe ultimatum.</summary>
    public static readonly EventKind Ultimatum = HistoryEventKind.Ultimatum;
    /// <summary>Submission tribute.</summary>
    public static readonly EventKind Tribute = HistoryEventKind.Tribute;
    /// <summary>A faith rendered as tags.</summary>
    public static readonly EventKind Myth = HistoryEventKind.Myth;
    /// <summary>A settlement was founded.</summary>
    public static readonly EventKind SettlementFounded = HistoryEventKind.SettlementFounded;
    /// <summary>A settlement ended.</summary>
    public static readonly EventKind SettlementEnded = HistoryEventKind.SettlementEnded;
    /// <summary>A shock struck places.</summary>
    public static readonly EventKind Shock = HistoryEventKind.Shock;
    /// <summary>A war took a settlement.</summary>
    public static readonly EventKind SettlementCaptured = HistoryEventKind.SettlementCaptured;
    /// <summary>A settlement grew or shrank one tier.</summary>
    public static readonly EventKind SettlementTierChanged = HistoryEventKind.SettlementTierChanged;
    /// <summary>A polity collapsed into successors.</summary>
    public static readonly EventKind PolityCollapsed = HistoryEventKind.PolityCollapsed;
    /// <summary>A polity was absorbed by a neighbour.</summary>
    public static readonly EventKind PolityAbsorbed = HistoryEventKind.PolityAbsorbed;
    /// <summary>A plugin-defined event; the name is on <see cref="HistoryEvent.PluginKind"/>.</summary>
    public static readonly EventKind Plugin = HistoryEventKind.Plugin;
    /// <summary>A siege ended.</summary>
    public static readonly EventKind Siege = HistoryEventKind.Siege;
    /// <summary>A person rose.</summary>
    public static readonly EventKind PersonRose = HistoryEventKind.PersonRose;
    /// <summary>A person died.</summary>
    public static readonly EventKind PersonDied = HistoryEventKind.PersonDied;
    /// <summary>A plot or covert act.</summary>
    public static readonly EventKind Intrigue = HistoryEventKind.Intrigue;
    /// <summary>Outlaws struck a caravan.</summary>
    public static readonly EventKind CaravanRaided = HistoryEventKind.CaravanRaided;
    /// <summary>An outlaw band formed.</summary>
    public static readonly EventKind OutlawRose = HistoryEventKind.OutlawRose;
    /// <summary>An outlaw band ended.</summary>
    public static readonly EventKind OutlawEnded = HistoryEventKind.OutlawEnded;
    /// <summary>Goods moved over a severed route.</summary>
    public static readonly EventKind Smuggling = HistoryEventKind.Smuggling;
    /// <summary>A licensed raider struck at sea.</summary>
    public static readonly EventKind Privateering = HistoryEventKind.Privateering;
    /// <summary>Campaign hardship.</summary>
    public static readonly EventKind Campaign = HistoryEventKind.Campaign;
    /// <summary>A prophet spoke.</summary>
    public static readonly EventKind Prophecy = HistoryEventKind.Prophecy;
}
