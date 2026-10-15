namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Kinds of entries in the append-only <see cref="HistoryCatalog.Events"/> log. P2a writes
/// foundings, forks and site losses; the P2b conflict operators add war, conquest, rebellion,
/// road and exchange kinds; the P2c treaty/faith models add treaty, grudge, holy, desecration,
/// villain, ultimatum, tribute and myth kinds.
/// </summary>
public enum HistoryEventKind : byte
{
    /// <summary>A polity was founded on a tile.</summary>
    Founding = 0,
    /// <summary>A map edit changed tile ownership: the timeline forked, causes and dates kept.</summary>
    Fork = 1,
    /// <summary>A projected settlement or site went inactive (drowned capital, lost ground).</summary>
    SiteLost = 2,
    /// <summary>One polity raided another. Tags <c>attacker:{id}</c> and <c>defender:{id}</c>; the tile is the battlefield.</summary>
    War = 3,
    /// <summary>A war ended in a culture overlay: the loser renders through the winner's culture. Tags <c>winner:{id}</c>, <c>loser:{id}</c> and the winning culture key.</summary>
    Conquest = 4,
    /// <summary>A polity split: a far-flung, oppressed region broke away. Tags <c>parent:{id}</c> plus the rebel folk and culture keys; the tile is the rebel capital.</summary>
    Rebellion = 5,
    /// <summary>A trade route opened between two capitals. Tags <c>built</c> and <c>route:{a}-{b}</c>.</summary>
    RoadBuilt = 6,
    /// <summary>A war severed a trade route. Tags <c>cut</c> and <c>route:{a}-{b}</c>.</summary>
    RoadCut = 7,
    /// <summary>Diffusion along an active route: loanwords and openness drift. Tags <c>loanword</c> and <c>route:{a}-{b}</c>.</summary>
    Exchange = 8,
    /// <summary>An abstract adult-only alliance contract (marriage or foster) between two polities. Tags <c>marriage|foster</c>, <c>party:{a}</c>, <c>party:{b}</c>, heir and claim-hook tags.</summary>
    Treaty = 9,
    /// <summary>A refused treaty logged as lasting casus belli. Tags <c>against:{id}</c> and the weight; cleared only when repaid (treaty, tribute or overlay).</summary>
    Grudge = 10,
    /// <summary>A polity raised a holy site. Tags <c>holy</c> and the faith form; the tile projects as a shrine.</summary>
    HolyFound = 11,
    /// <summary>A war struck a holy tile. Tags the desecrator, the holy owner and the tile; raises grudges on both sides plus a coalition.</summary>
    Desecration = 12,
    /// <summary>A long-life actor rose to rule a polity. Tags the villain id and agenda; the polity sees no further successions.</summary>
    Villain = 13,
    /// <summary>A steppe ultimatum: submit-and-pay or resist. Tags <c>submit|resist</c>, the khan and the weaker polity.</summary>
    Ultimatum = 14,
    /// <summary>Submission tribute after an ultimatum. Tags <c>from:{id}</c> and <c>to:{id}</c>; carries a non-aggression window.</summary>
    Tribute = 15,
    /// <summary>A polity's faith rendered as tags: form, powers and myth slots. The text contract for <c>IMythTextProvider</c>; stubbing it never changes the log.</summary>
    Myth = 16,
}
