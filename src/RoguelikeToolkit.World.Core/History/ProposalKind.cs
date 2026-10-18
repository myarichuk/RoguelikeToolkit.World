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
    /// <summary>Add a signed amount to a standing settlement's population.</summary>
    GrowSettlement = 18,
    /// <summary>Move a share of a standing settlement's people into the displaced pool.</summary>
    ReleasePopulation = 19,
    /// <summary>Settle people from the displaced pool into a standing settlement.</summary>
    AbsorbDisplaced = 20,
    /// <summary>People of a standing settlement die (the only other non-conserving sink besides pool decay).</summary>
    KillPopulation = 21,
    /// <summary>Leave a scar (ruin, crater, plague pit...) on a tile.</summary>
    AddScar = 22,
    /// <summary>A settlement changes hands in war.</summary>
    CaptureSettlement = 23,
    /// <summary>Move a standing settlement one step up or down the tier ladder.</summary>
    ChangeTier = 24,
    /// <summary>A polity ends and its territory passes to successor polities. Applied in the structural pre-pass.</summary>
    SplitPolity = 25,
    /// <summary>A polity ends and a neighbour takes over its land and settlements. Applied in the structural pre-pass.</summary>
    AbsorbPolity = 26,
    /// <summary>Store a blob in the plugin state bag.</summary>
    SetPluginState = 27,
}
