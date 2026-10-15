namespace RoguelikeToolkit.World.Core.History;

/// <summary>
/// Kind of a <see cref="Proposal"/>. The numeric order is the commit order within one entity, so
/// ownership and structure change before events are written and scalar deltas apply last.
/// </summary>
public enum ProposalKind
{
    /// <summary>Move a tile to a new owner.</summary>
    ReassignTile = 0,
    /// <summary>Open a trade-road segment.</summary>
    AddRoad = 1,
    /// <summary>Sever a trade-road segment.</summary>
    SeverRoute = 2,
    /// <summary>Raise a holy site.</summary>
    AddHolySite = 3,
    /// <summary>Add weight to a grudge.</summary>
    AddGrudge = 4,
    /// <summary>Repay the grudge between two polities.</summary>
    RepayGrudge = 5,
    /// <summary>Open a non-aggression window.</summary>
    AddWindow = 6,
    /// <summary>Append an event.</summary>
    AppendEvent = 7,
    /// <summary>Add a delta to a declared scalar.</summary>
    AdjustScalar = 8,
    /// <summary>Set a polity's population index and ruler generation.</summary>
    SetPopulation = 9,
    /// <summary>Record a historic site (battlefield) on a tile.</summary>
    AddSite = 10,
    /// <summary>Set a polity's culture overlay.</summary>
    SetOverlay = 11,
    /// <summary>Record the birth turn of a polity's treaty heir.</summary>
    SetHeir = 12,
    /// <summary>Found a new polity. Applied in a structural pre-pass, before every other proposal of the turn.</summary>
    AddPolity = 13,
    /// <summary>A villain rises to rule a polity; applied after population so its ruler generation wins.</summary>
    AddVillain = 14,
    /// <summary>Nudge a polity's openness drift up by one, up to the bound.</summary>
    DriftOpenness = 15,
    /// <summary>Found a settlement for a polity.</summary>
    AddSettlement = 16,
    /// <summary>End a standing settlement.</summary>
    EndSettlement = 17,
}
