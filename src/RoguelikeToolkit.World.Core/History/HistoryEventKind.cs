namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Kinds of entries in the append-only <see cref="HistoryCatalog.Events"/> log. P2a writes
/// foundings, forks and site losses; the P2b conflict operators add war, conquest, rebellion,
/// road and exchange kinds; the P2c treaty/faith models add treaty, emergence and myth kinds.
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
}
